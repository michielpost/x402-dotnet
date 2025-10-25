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

    public async Task<HandleX402Result> HandleX402Async(
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

    public async Task<HandleX402Result> HandleX402Async(
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

        if (string.IsNullOrEmpty(header))
        {
            logger.LogInformation("No X-PAYMENT header present for path {Path}; responding 402", fullUrl);
            return await ReturnErrorAsync(context, paymentRequirements, StatusCodes.Status402PaymentRequired, "X-PAYMENT header is required");
        }

        try
        {
            var payload = PaymentPayloadHeader.FromHeader(header);
            logger.LogDebug("Parsed X-PAYMENT header for path {Path}", fullUrl);

            var validationResult = await ValidatePayload(paymentRequirements, payload, fullUrl);
            if (!validationResult.CanContinueRequest)
            {
                return await ReturnErrorAsync(context, paymentRequirements, StatusCodes.Status402PaymentRequired, validationResult.Error);
            }

            var vr = await facilitator.VerifyAsync(payload, paymentRequirements);
            logger.LogInformation("Verification completed for path {Path}: IsValid={IsValid}", fullUrl, vr.IsValid);

            if (!vr.IsValid)
            {
                logger.LogInformation("Verification invalid for path {Path}: {Reason}", fullUrl, vr.InvalidReason);
                return await ReturnErrorAsync(context, paymentRequirements, StatusCodes.Status402PaymentRequired, vr.InvalidReason, vr);
            }

            SettlementResponse? preSettledResponse = null;
            Exception? settlementException = null;

            if (settlementMode == SettlementMode.Pessimistic)
            {
                (preSettledResponse, settlementException) = await HandlePessimisticSettlement(payload, paymentRequirements, fullUrl);
                if (settlementException != null || preSettledResponse == null || !preSettledResponse.Success)
                {
                    var errorMsg = preSettledResponse?.ErrorReason ?? settlementException?.Message ?? FacilitatorErrorCodes.UnexpectedSettleError;
                    return await ReturnErrorAsync(context, paymentRequirements, StatusCodes.Status402PaymentRequired, errorMsg, vr, preSettledResponse);
                }
                await InvokeSettlementCallback(context, onSettlement, preSettledResponse, settlementException, fullUrl);
            }

            context.Response.OnStarting(async () =>
            {
                SettlementResponse? sr = preSettledResponse;
                Exception? innerSettlementException = null;
                try
                {
                    if (sr == null)
                    {
                        sr = await facilitator.SettleAsync(payload, paymentRequirements);
                        if (sr == null || !sr.Success)
                        {
                            var errorMsg = sr?.ErrorReason ?? FacilitatorErrorCodes.UnexpectedSettleError;
                            logger.LogWarning("Settlement failed for path {Path}: {Reason}", fullUrl, errorMsg);
                            if (settlementMode == SettlementMode.Pessimistic && !context.Response.HasStarted)
                            {
                                await Respond402Async(context, paymentRequirements, errorMsg);
                            }
                            return;
                        }
                    }

                    AppendPaymentResponseHeader(context, sr, payload.ExtractPayerFromPayload(), fullUrl);
                }
                catch (Exception ex)
                {
                    innerSettlementException = ex;
                    logger.LogError(ex, "Settlement error for path {Path}", fullUrl);
                    if (settlementMode == SettlementMode.Pessimistic && !context.Response.HasStarted)
                    {
                        await Respond402Async(context, paymentRequirements, "settlement error: " + ex.Message);
                    }
                    return;
                }
                finally
                {
                    if (preSettledResponse == null && onSettlement != null)
                    {
                        await InvokeSettlementCallback(context, onSettlement, sr, innerSettlementException, fullUrl);
                    }
                }
            });

            logger.LogDebug("Payment verified; proceeding to response for path {Path}", fullUrl);
            var finalResult = new HandleX402Result(preSettledResponse?.Success ?? vr.IsValid, paymentRequirements, null, vr, preSettledResponse);
            StoreResult(finalResult);
            return finalResult;
        }
        catch (ArgumentException)
        {
            logger.LogWarning("Malformed X-PAYMENT header for path {Path}", fullUrl);
            return await ReturnErrorAsync(context, paymentRequirements, StatusCodes.Status402PaymentRequired, "Malformed X-PAYMENT header");
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Payment verification IO error for path {Path}", fullUrl);
            return await ReturnErrorAsync(context, paymentRequirements, StatusCodes.Status500InternalServerError, $"Payment verification failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during payment verification for path {Path}", fullUrl);
            return await ReturnErrorAsync(context, paymentRequirements, StatusCodes.Status500InternalServerError, $"Internal server error during payment verification. {ex.Message}");
        }
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

    private async Task<HandleX402Result> ReturnErrorAsync(
        HttpContext context,
        PaymentRequirements paymentRequirements,
        int statusCode,
        string? errorMessage,
        VerificationResponse? vr = null,
        SettlementResponse? sr = null)
    {
        if (!context.Response.HasStarted)
        {
            if (statusCode == StatusCodes.Status402PaymentRequired)
                await Respond402Async(context, paymentRequirements, errorMessage);
            else
                await Respond500Async(context, errorMessage);
        }
        else
        {
            logger.LogWarning("Cannot modify response for path {Path}; response already started", paymentRequirements.Resource);
        }

        var result = new HandleX402Result(false, paymentRequirements, errorMessage, vr, sr);
        StoreResult(result);
        return result;
    }

    private void StoreResult(HandleX402Result result)
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

    private async Task<HandleX402Result> ValidatePayload(PaymentRequirements paymentRequirements, PaymentPayloadHeader payload, string fullUrl)
    {
        if (!string.IsNullOrEmpty(payload.Payload.Resource) && !string.Equals(payload.Payload.Resource, fullUrl, StringComparison.InvariantCultureIgnoreCase))
        {
            logger.LogWarning("Resource mismatch: payload {PayloadResource} vs request {RequestPath}", payload.Payload.Resource, fullUrl);
            return new HandleX402Result(false, paymentRequirements, $"Resource mismatch: payload {payload.Payload.Resource} vs request {fullUrl}");
        }

        if (payload.Scheme != paymentRequirements.Scheme)
        {
            logger.LogWarning("Scheme mismatch: payload {PayloadScheme} vs requirements {RequirementsScheme}", payload.Scheme, paymentRequirements.Scheme);
            return new HandleX402Result(false, paymentRequirements, $"Scheme mismatch: payload {payload.Scheme} vs requirements {paymentRequirements.Scheme}");
        }

        if (payload.Network != paymentRequirements.Network)
        {
            logger.LogWarning("Network mismatch: payload {PayloadNetwork} vs requirements {RequirementsNetwork}", payload.Network, paymentRequirements.Network);
            return new HandleX402Result(false, paymentRequirements, $"Network mismatch: payload {payload.Network} vs requirements {paymentRequirements.Network}");
        }

        var authorization = payload.Payload.Authorization;
        if (authorization.To != paymentRequirements.PayTo)
        {
            logger.LogWarning("PayTo mismatch: authorization {AuthorizationTo} vs requirements {RequirementsPayTo}", authorization.To, paymentRequirements.PayTo);
            return new HandleX402Result(false, paymentRequirements, $"PayTo mismatch: authorization {authorization.To} vs requirements {paymentRequirements.PayTo}");
        }

        if (authorization.Value != paymentRequirements.MaxAmountRequired)
        {
            logger.LogWarning("Amount mismatch: authorization {AuthorizationValue} vs requirements {RequirementsAmount}", authorization.Value, paymentRequirements.MaxAmountRequired);
            return new HandleX402Result(false, paymentRequirements, $"Amount mismatch: authorization {authorization.Value} vs requirements {paymentRequirements.MaxAmountRequired}");
        }

        return new HandleX402Result(true, paymentRequirements);
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