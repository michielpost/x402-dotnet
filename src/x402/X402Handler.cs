using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using x402.Core.Enums;
using x402.Core.Interfaces;
using x402.Core.Models;
using x402.Core.Models.Facilitator;
using x402.Core.Models.Responses;
using x402.Facilitator;

namespace x402;

public class X402Handler
{
    public static readonly string X402ResultKey = "X402HandleResult";

    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<X402Handler> logger;
    private readonly IFacilitatorClient facilitator;
    private readonly IAssetInfoProvider assetInfoProvider;
    private readonly IHttpContextAccessor httpContextAccessor;

    public X402Handler(
        ILogger<X402Handler> logger,
        IFacilitatorClient facilitator,
        IAssetInfoProvider assetInfoProvider,
        IHttpContextAccessor httpContextAccessor)
    {
        this.logger = logger;
        this.facilitator = facilitator;
        this.assetInfoProvider = assetInfoProvider;
        this.httpContextAccessor = httpContextAccessor;
    }

    public async Task<X402ProcessingResult> HandleX402Async(
        PaymentRequirementsBasic paymentRequirementsBasic,
        bool discoverable,
        SettlementMode settlementMode = SettlementMode.Optimistic,
        Func<HttpContext, SettlementResponse?, Exception?, Task>? onSettlement = null,
        Func<HttpContext, PaymentRequirements, OutputSchema, OutputSchema>? onSetOutputSchema = null)
    {
        var paymentRequirements = FillPaymentRequirements(paymentRequirementsBasic);
        var result = await HandleX402Async(paymentRequirements, discoverable, settlementMode, onSettlement, onSetOutputSchema);
        StoreResult(result);
        return result;
    }

    public async Task<X402ProcessingResult> HandleX402Async(
        PaymentRequirements paymentRequirements,
        bool discoverable,
        SettlementMode settlementMode = SettlementMode.Optimistic,
        Func<HttpContext, SettlementResponse?, Exception?, Task>? onSettlement = null,
        Func<HttpContext, PaymentRequirements, OutputSchema, OutputSchema>? onSetOutputSchema = null)
    {
        var context = GetHttpContext();
        var fullUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}".ToLowerInvariant();
        paymentRequirements.Resource = fullUrl;
        logger.LogDebug("HandleX402 invoked for path {Path}", fullUrl);

        string? header = context.Request.Headers["X-PAYMENT"].FirstOrDefault();
        var outputSchema = new OutputSchema
        {
            Input = new Input
            {
                Discoverable = discoverable,
                Type = "http",
                Method = context.Request.Method
            }
        };

        if (onSetOutputSchema != null)
        {
            try
            {
                outputSchema = onSetOutputSchema(context, paymentRequirements, outputSchema);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "onSetOutputSchema callback threw for path {Path}", fullUrl);
            }
        }
        paymentRequirements.OutputSchema = outputSchema;

        // Process the payment logic
        var processingResult = await ProcessPaymentAsync(paymentRequirements, header, fullUrl, settlementMode);

        // Handle HTTP response based on processing result
        await HandleHttpResponseAsync(context, processingResult, settlementMode, onSettlement);

        StoreResult(processingResult);
        return processingResult;
    }

    /// <summary>
    /// Processes payment logic without HTTP-specific concerns.
    /// </summary>
    private async Task<X402ProcessingResult> ProcessPaymentAsync(
        PaymentRequirements paymentRequirements,
        string? paymentHeader,
        string fullUrl,
        SettlementMode settlementMode)
    {
        if (string.IsNullOrEmpty(paymentHeader))
        {
            logger.LogInformation("No X-PAYMENT header present for path {Path}; responding 402", fullUrl);
            return X402ProcessingResult.CreateError(
                paymentRequirements,
                "X-PAYMENT header is required",
                StatusCodes.Status402PaymentRequired,
                fullUrl: fullUrl);
        }

        try
        {
            var payload = PaymentPayloadHeader.FromHeader(paymentHeader);
            logger.LogDebug("Parsed X-PAYMENT header for path {Path}", fullUrl);

            var validationResult = await ValidatePayload(paymentRequirements, payload, fullUrl);
            if (!validationResult.CanContinueRequest)
            {
                return X402ProcessingResult.CreateError(
                    paymentRequirements,
                    validationResult.Error ?? "Validation failed",
                    StatusCodes.Status402PaymentRequired,
                    fullUrl: fullUrl);
            }

            var vr = await facilitator.VerifyAsync(payload, paymentRequirements);
            logger.LogInformation("Verification completed for path {Path}: IsValid={IsValid}", fullUrl, vr.IsValid);

            if (!vr.IsValid)
            {
                logger.LogInformation("Verification invalid for path {Path}: {Reason}", fullUrl, vr.InvalidReason);
                return X402ProcessingResult.CreateError(
                    paymentRequirements,
                    vr.InvalidReason ?? "Verification failed",
                    StatusCodes.Status402PaymentRequired,
                    vr,
                    fullUrl: fullUrl);
            }

            SettlementResponse? preSettledResponse = null;
            Exception? settlementException = null;

            if (settlementMode == SettlementMode.Pessimistic)
            {
                (preSettledResponse, settlementException) = await HandlePessimisticSettlement(payload, paymentRequirements, fullUrl);
                if (settlementException != null || preSettledResponse == null || !preSettledResponse.Success)
                {
                    var errorMsg = preSettledResponse?.ErrorReason ?? settlementException?.Message ?? FacilitatorErrorCodes.UnexpectedSettleError;
                    return X402ProcessingResult.CreateError(
                        paymentRequirements,
                        errorMsg,
                        StatusCodes.Status402PaymentRequired,
                        vr,
                        preSettledResponse,
                        payload,
                        fullUrl,
                        settlementException);
                }
            }

            logger.LogDebug("Payment verified; proceeding to response for path {Path}", fullUrl);
            return X402ProcessingResult.Success(
                paymentRequirements,
                vr,
                preSettledResponse,
                payload,
                fullUrl,
                settlementMode == SettlementMode.Pessimistic);
        }
        catch (ArgumentException)
        {
            logger.LogWarning("Malformed X-PAYMENT header for path {Path}", fullUrl);
            return X402ProcessingResult.CreateError(
                paymentRequirements,
                "Malformed X-PAYMENT header",
                StatusCodes.Status402PaymentRequired,
                fullUrl: fullUrl);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Payment verification IO error for path {Path}", fullUrl);
            return X402ProcessingResult.CreateError(
                paymentRequirements,
                $"Payment verification failed: {ex.Message}",
                StatusCodes.Status500InternalServerError,
                fullUrl: fullUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during payment verification for path {Path}", fullUrl);
            return X402ProcessingResult.CreateError(
                paymentRequirements,
                $"Internal server error during payment verification. {ex.Message}",
                StatusCodes.Status500InternalServerError,
                fullUrl: fullUrl);
        }
    }

    /// <summary>
    /// Handles HTTP response based on the processing result.
    /// </summary>
    private async Task HandleHttpResponseAsync(
        HttpContext context,
        X402ProcessingResult processingResult,
        SettlementMode settlementMode,
        Func<HttpContext, SettlementResponse?, Exception?, Task>? onSettlement)
    {
        if (!processingResult.CanContinueRequest)
        {
            // Handle error responses
            if (!context.Response.HasStarted)
            {
                if (processingResult.StatusCode == StatusCodes.Status402PaymentRequired)
                    await Respond402Async(context, processingResult.PaymentRequirements, processingResult.Error);
                else
                    await Respond500Async(context, processingResult.Error);
            }
            else
            {
                logger.LogWarning("Cannot modify response for path {Path}; response already started", processingResult.FullUrl);
            }
            return;
        }

        // Handle successful responses
        if (processingResult.PessimisticSettlement && processingResult.SettlementResponse != null)
        {
            // Settlement was already done pessimistically, invoke callback
            await InvokeSettlementCallback(context, onSettlement, processingResult.SettlementResponse, processingResult.SettlementException, processingResult.FullUrl);
        }

        // Set up optimistic settlement for when response starts
        context.Response.OnStarting(async () =>
        {
            SettlementResponse? sr = processingResult.SettlementResponse;
            Exception? innerSettlementException = null;
            try
            {
                if (sr == null && processingResult.PaymentPayload != null)
                {
                    sr = await facilitator.SettleAsync(processingResult.PaymentPayload, processingResult.PaymentRequirements);
                    if (sr == null || !sr.Success)
                    {
                        var errorMsg = sr?.ErrorReason ?? FacilitatorErrorCodes.UnexpectedSettleError;
                        logger.LogWarning("Settlement failed for path {Path}: {Reason}", processingResult.FullUrl, errorMsg);
                        if (settlementMode == SettlementMode.Pessimistic && !context.Response.HasStarted)
                        {
                            await Respond402Async(context, processingResult.PaymentRequirements, errorMsg);
                        }
                        return;
                    }
                }

                if (sr != null)
                {
                    AppendPaymentResponseHeader(context, sr, processingResult.PaymentPayload?.ExtractPayerFromPayload(), processingResult.FullUrl);
                }
            }
            catch (Exception ex)
            {
                innerSettlementException = ex;
                logger.LogError(ex, "Settlement error for path {Path}", processingResult.FullUrl);
                if (settlementMode == SettlementMode.Pessimistic && !context.Response.HasStarted)
                {
                    await Respond402Async(context, processingResult.PaymentRequirements, "settlement error: " + ex.Message);
                }
                return;
            }
            finally
            {
                if (!processingResult.PessimisticSettlement && onSettlement != null)
                {
                    await InvokeSettlementCallback(context, onSettlement, sr, innerSettlementException, processingResult.FullUrl);
                }
            }
        });
    }

    private async Task<(SettlementResponse?, Exception?)> HandlePessimisticSettlement(
        PaymentPayloadHeader payload, PaymentRequirements paymentRequirements, string fullUrl)
    {
        try
        {
            var response = await facilitator.SettleAsync(payload, paymentRequirements);
            if (response == null || !response.Success)
            {
                var errorMsg = response?.ErrorReason ?? FacilitatorErrorCodes.UnexpectedSettleError;
                logger.LogWarning("Pessimistic settlement failed for path {Path}: {Reason}", fullUrl, errorMsg);
            }
            return (response, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pessimistic settlement error for path {Path}", fullUrl);
            return (null, ex);
        }
    }

    private async Task InvokeSettlementCallback(
        HttpContext context,
        Func<HttpContext, SettlementResponse?, Exception?, Task>? onSettlement,
        SettlementResponse? response,
        Exception? exception,
        string fullUrl)
    {
        if (onSettlement != null)
        {
            try
            {
                await onSettlement(context, response, exception);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "onSettlement callback threw for path {Path}", fullUrl);
            }
        }
    }

    private void AppendPaymentResponseHeader(HttpContext context, SettlementResponse sr, string? payer, string fullUrl)
    {
        try
        {
            var base64Header = CreatePaymentResponseHeader(sr, payer);
            context.Response.Headers.Append("X-PAYMENT-RESPONSE", base64Header);
            context.Response.Headers.Append("Access-Control-Expose-Headers", "X-PAYMENT-RESPONSE");
            logger.LogInformation("Settlement succeeded for path {Path}; response header appended (payer={Payer})", fullUrl, payer);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create settlement response header for path {Path}", fullUrl);
            throw;
        }
    }


    private void StoreResult(X402ProcessingResult result)
    {
        if (httpContextAccessor.HttpContext != null)
        {
            httpContextAccessor.HttpContext.Items[X402ResultKey] = result;
        }
    }

    private HttpContext GetHttpContext()
    {
        var context = httpContextAccessor.HttpContext;
        if (context == null)
        {
            throw new InvalidOperationException("HttpContext is not available.");
        }
        return context;
    }

    private async Task<X402ProcessingResult> ValidatePayload(PaymentRequirements paymentRequirements, PaymentPayloadHeader payload, string fullUrl)
    {
        if (!string.IsNullOrEmpty(payload.Payload.Resource) && !string.Equals(payload.Payload.Resource, fullUrl, StringComparison.InvariantCultureIgnoreCase))
        {
            logger.LogWarning("Resource mismatch: payload {PayloadResource} vs request {RequestPath}", payload.Payload.Resource, fullUrl);
            return X402ProcessingResult.CreateError(
                paymentRequirements,
                $"Resource mismatch: payload {payload.Payload.Resource} vs request {fullUrl}",
                StatusCodes.Status402PaymentRequired,
                fullUrl: fullUrl);
        }

        if (payload.Scheme != paymentRequirements.Scheme)
        {
            logger.LogWarning("Scheme mismatch: payload {PayloadScheme} vs requirements {RequirementsScheme}", payload.Scheme, paymentRequirements.Scheme);
            return X402ProcessingResult.CreateError(
                paymentRequirements,
                $"Scheme mismatch: payload {payload.Scheme} vs requirements {paymentRequirements.Scheme}",
                StatusCodes.Status402PaymentRequired,
                fullUrl: fullUrl);
        }

        if (payload.Network != paymentRequirements.Network)
        {
            logger.LogWarning("Network mismatch: payload {PayloadNetwork} vs requirements {RequirementsNetwork}", payload.Network, paymentRequirements.Network);
            return X402ProcessingResult.CreateError(
                paymentRequirements,
                $"Network mismatch: payload {payload.Network} vs requirements {paymentRequirements.Network}",
                StatusCodes.Status402PaymentRequired,
                fullUrl: fullUrl);
        }

        var authorization = payload.Payload.Authorization;
        if (authorization.To != paymentRequirements.PayTo)
        {
            logger.LogWarning("PayTo mismatch: authorization {AuthorizationTo} vs requirements {RequirementsPayTo}", authorization.To, paymentRequirements.PayTo);
            return X402ProcessingResult.CreateError(
                paymentRequirements,
                $"PayTo mismatch: authorization {authorization.To} vs requirements {paymentRequirements.PayTo}",
                StatusCodes.Status402PaymentRequired,
                fullUrl: fullUrl);
        }

        if (authorization.Value != paymentRequirements.MaxAmountRequired)
        {
            logger.LogWarning("Amount mismatch: authorization {AuthorizationValue} vs requirements {RequirementsAmount}", authorization.Value, paymentRequirements.MaxAmountRequired);
            return X402ProcessingResult.CreateError(
                paymentRequirements,
                $"Amount mismatch: authorization {authorization.Value} vs requirements {paymentRequirements.MaxAmountRequired}",
                StatusCodes.Status402PaymentRequired,
                fullUrl: fullUrl);
        }

        var hasValidBefore = long.TryParse(authorization.ValidBefore, out long validBefore);
        if (!hasValidBefore || validBefore < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            logger.LogWarning("Authorization expired: validBefore {ValidBefore} is in the past", authorization.ValidBefore);
            return X402ProcessingResult.CreateError(
                paymentRequirements,
                $"Authorization expired: validBefore {authorization.ValidBefore} is in the past",
                StatusCodes.Status402PaymentRequired,
                fullUrl: fullUrl);
        }

        var hasValidAfter = long.TryParse(authorization.ValidAfter, out long validAfter);
        if(!hasValidAfter || validAfter > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            logger.LogWarning("Authorization not yet valid: validAfter {ValidAfter} is in the future", authorization.ValidAfter);
            return X402ProcessingResult.CreateError(
                paymentRequirements,
                $"Authorization not yet valid: validAfter {authorization.ValidAfter} is in the future",
                StatusCodes.Status402PaymentRequired,
                fullUrl: fullUrl);
        }


        return X402ProcessingResult.Success(paymentRequirements, null!, fullUrl: fullUrl);
    }

    private string CreatePaymentResponseHeader(SettlementResponse sr, string? payer)
    {
        var settlementHeader = new SettlementResponseHeader(
            true,
            sr.Transaction ?? string.Empty,
            sr.Network ?? string.Empty,
            payer
        );

        string jsonString = JsonSerializer.Serialize(settlementHeader, jsonOptions);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonString));
    }

    private Task Respond402Async(HttpContext context, PaymentRequirements paymentRequirements, string? error)
    {
        if (context.Response.HasStarted)
        {
            logger.LogWarning("Response already started; cannot write");
            return Task.CompletedTask;
        }

        context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
        context.Response.ContentType = "application/json";

        var prr = new PaymentRequiredResponse
        {
            X402Version = 1,
            Accepts = new List<PaymentRequirements> { paymentRequirements },
            Error = error
        };

        string json = JsonSerializer.Serialize(prr, jsonOptions);
        return context.Response.WriteAsync(json);
    }

    private Task Respond500Async(HttpContext context, string? errorMsg)
    {
        if (context.Response.HasStarted)
        {
            logger.LogWarning("Response already started; cannot write error body");
            return Task.CompletedTask;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        string json = "{\"error\":\"" + errorMsg + "\"}";
        return context.Response.WriteAsync(json);
    }

    private PaymentRequirements FillPaymentRequirements(PaymentRequirementsBasic basic)
    {
        var assetInfo = assetInfoProvider.GetAssetInfo(basic.Asset);
        if (assetInfo == null)
        {
            logger.LogWarning("No asset info found for asset {Asset}", basic.Asset);
        }

        return new PaymentRequirements
        {
            Scheme = basic.Scheme,
            Network = assetInfo?.Network ?? string.Empty,
            MaxAmountRequired = basic.MaxAmountRequired,
            Asset = basic.Asset,
            MimeType = basic.MimeType,
            PayTo = basic.PayTo,
            MaxTimeoutSeconds = basic.MaxTimeoutSeconds,
            Description = basic.Description,
            Extra = new PaymentRequirementsExtra
            {
                Name = assetInfo?.Name ?? string.Empty,
                Version = assetInfo?.Version ?? string.Empty
            }
        };
    }
}