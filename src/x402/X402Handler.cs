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
    // Key for storing HandleX402Result in HttpContext.Items
    public static readonly string X402ResultKey = "X402HandleResult";

    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // default camelCase
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<X402Handler> logger;
    private readonly IFacilitatorClient facilitator;
    private readonly ITokenInfoProvider tokenInfoProvider;
    private readonly IHttpContextAccessor httpContextAccessor;

    public X402Handler(ILogger<X402Handler> logger,
        IFacilitatorClient facilitator,
        ITokenInfoProvider tokenInfoProvider,
        IHttpContextAccessor httpContextAccessor)
    {
        this.logger = logger;
        this.facilitator = facilitator;
        this.tokenInfoProvider = tokenInfoProvider;
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

        // Store the result in HttpContext.Items
        if (httpContextAccessor.HttpContext != null)
        {
            httpContextAccessor.HttpContext.Items[X402ResultKey] = result;
        }

        return result;
    }


    public async Task<HandleX402Result> HandleX402Async(
        PaymentRequirements paymentRequirements,
        bool discoverable,
        SettlementMode settlementMode = SettlementMode.Optimistic,
        Func<HttpContext, SettlementResponse?, Exception?, Task>? onSettlement = null,
        Func<HttpContext, PaymentRequirements, OutputSchema, OutputSchema>? onSetOutputSchema = null)
    {
        var context = httpContextAccessor.HttpContext;
        if (context == null)
        {
            throw new InvalidOperationException("HttpContext is not available.");
        }
        var request = context.Request;
        var fullUrl = $"{request.Scheme}://{request.Host}{request.Path}{request.QueryString}".ToLowerInvariant();

        paymentRequirements.Resource = fullUrl;

        logger.LogDebug("HandleX402 invoked for path {Path}", fullUrl);
        string? header = context.Request.Headers["X-PAYMENT"].FirstOrDefault();

        var outputSchema = new OutputSchema()
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


        //No payment, return 402
        if (string.IsNullOrEmpty(header))
        {
            logger.LogInformation("No X-PAYMENT header present for path {Path}; responding 402", fullUrl);
            await Respond402Async(context, paymentRequirements, "X-PAYMENT header is required");
            var result = new HandleX402Result(false);
            context.Items[X402ResultKey] = result; // Store result
            return result;

        }

        //Handle payment verification
        PaymentPayloadHeader? payload = null;
        VerificationResponse? vr = null;
        try
        {
            payload = PaymentPayloadHeader.FromHeader(header);
            logger.LogDebug("Parsed X-PAYMENT header for path {Path}", fullUrl);

            HandleX402Result validationResult = await ValidatePayload(paymentRequirements, payload, fullUrl);
            if (!validationResult.CanContinueRequest)
            {
                if (context.Response.HasStarted)
                {
                    logger.LogWarning("Cannot modify response for path {Path}; response already started", fullUrl);
                }
                else
                {
                    await Respond402Async(context, paymentRequirements, validationResult.Error);
                }
                context.Items[X402ResultKey] = validationResult; // Store result
                return validationResult;
            }

            // Verify payment with facilitator
            vr = await facilitator.VerifyAsync(payload, paymentRequirements);
            logger.LogInformation("Verification completed for path {Path}: IsValid={IsValid}", fullUrl, vr.IsValid);
        }
        catch (ArgumentException)
        {
            // Malformed payment header - client error
            logger.LogWarning("Malformed X-PAYMENT header for path {Path}", fullUrl);
            await Respond402Async(context, paymentRequirements, "Malformed X-PAYMENT header"); 
            var result = new HandleX402Result(false, "Malformed X-PAYMENT header", vr);
            context.Items[X402ResultKey] = result; // Store result
            return result;

        }
        catch (IOException ex)
        {
            // Network/communication error with facilitator - server error
            logger.LogError(ex, "Payment verification IO error for path {Path}", fullUrl);
            await Respond500Async(context, "Payment verification failed: " + ex.Message); 
            var result = new HandleX402Result(false, $"Payment verification failed: {ex.Message}", vr);
            context.Items[X402ResultKey] = result; // Store result
            return result;

        }
        catch (Exception ex)
        {
            // Other unexpected errors - server error
            logger.LogError(ex, "Unexpected error during payment verification for path {Path}", fullUrl);
            await Respond500Async(context, "Internal server error during payment verification");
            var result = new HandleX402Result(false, $"Internal server error during payment verification. {ex.Message}", vr);
            context.Items[X402ResultKey] = result; // Store result
            return result;
        }

        if (!vr.IsValid)
        {
            logger.LogInformation("Verification invalid for path {Path}: {Reason}", fullUrl, vr.InvalidReason);
            await Respond402Async(context, paymentRequirements, vr.InvalidReason);
            var result = new HandleX402Result(false, vr.InvalidReason, vr);
            context.Items[X402ResultKey] = result; // Store result
            return result;
        }

        // Optional pessimistic settlement in the main flow
        SettlementResponse? preSettledResponse = null;
        Exception? settlementException = null;
        if (settlementMode == SettlementMode.Pessimistic)
        {
            try
            {
                preSettledResponse = await facilitator.SettleAsync(payload, paymentRequirements);
                if (preSettledResponse == null || !preSettledResponse.Success)
                {
                    string errorMsg = preSettledResponse != null && preSettledResponse.ErrorReason != null
                        ? preSettledResponse.ErrorReason
                        : FacilitatorErrorCodes.UnexpectedSettleError;
                    logger.LogWarning("Pessimistic settlement failed for path {Path}: {Reason}", fullUrl, errorMsg);
                    await Respond402Async(context, paymentRequirements, errorMsg);
                    var result = new HandleX402Result(false, errorMsg, vr, preSettledResponse);
                    context.Items[X402ResultKey] = result; // Store result
                    return result;
                }
            }
            catch (Exception ex)
            {
                settlementException = ex;
                logger.LogError(ex, "Pessimistic settlement error for path {Path}", fullUrl);
                await Respond402Async(context, paymentRequirements, "settlement error: " + ex.Message);
                var result = new HandleX402Result(false, "settlement error: " + ex.Message, vr, preSettledResponse);
                context.Items[X402ResultKey] = result; // Store result
                return result;

            }
            finally
            {
                // Invoke callback early when available
                if (onSettlement != null)
                {
                    try
                    {
                        await onSettlement(context, preSettledResponse, settlementException);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "onSettlement callback threw for path {Path}", fullUrl);
                    }
                }
            }
        }

        // Settlement header must be written before any other content
        context.Response.OnStarting(async () =>
        {
            //Start settlement
            SettlementResponse? sr = preSettledResponse;
            Exception? settlementException = null;
            try
            {
                if (sr == null)
                {
                    sr = await facilitator.SettleAsync(payload, paymentRequirements);
                    if (sr == null || !sr.Success)
                    {
                        // Settlement failed
                        string errorMsg = sr != null && sr.ErrorReason != null ? sr.ErrorReason : FacilitatorErrorCodes.UnexpectedSettleError;
                        logger.LogWarning("Settlement failed for path {Path}: {Reason}", fullUrl, errorMsg);

                        // In pessimistic mode, downgrade to 402 if possible (headers not sent)
                        // In optimistic mode, do not alter the response status/body
                        if (settlementMode == SettlementMode.Pessimistic && !context.Response.HasStarted)
                        {
                            await Respond402Async(context, paymentRequirements, errorMsg);
                        }
                        return;
                    }
                }

                // Settlement succeeded - add settlement response header (base64-encoded JSON)
                try
                {
                    // Extract payer from payment payload (wallet address of person making payment)
                    string? payer = payload.ExtractPayerFromPayload();

                    string base64Header = CreatePaymentResponseHeader(sr, payer);
                    context.Response.Headers.Append("X-PAYMENT-RESPONSE", base64Header);

                    // Set CORS header to expose X-PAYMENT-RESPONSE to browser clients
                    context.Response.Headers.Append("Access-Control-Expose-Headers", "X-PAYMENT-RESPONSE");
                    logger.LogInformation("Settlement succeeded for path {Path}; response header appended (payer={Payer})", fullUrl, payer);


                }
                catch (Exception)
                {
                    // If header creation fails
                    logger.LogError("Failed to create settlement response header for path {Path}", fullUrl);
                    throw;
                }

            }
            catch (Exception ex)
            {
                settlementException = ex;

                // Network/communication errors during settlement
                if (settlementMode == SettlementMode.Pessimistic && !context.Response.HasStarted)
                {
                    logger.LogError(ex, "Settlement error for path {Path}", fullUrl);
                    await Respond402Async(context, paymentRequirements, "settlement error: " + ex.Message);
                }
                return;
            }
            finally
            {
                // Invoke callback when settlement completes (optimistic path only)
                if (preSettledResponse == null && onSettlement != null)
                {
                    try
                    {
                        await onSettlement(context, sr, settlementException);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "onSettlement callback threw for path {Path}", fullUrl);
                    }
                }
            }
        });

        logger.LogDebug("Payment verified; proceeding to response for path {Path}", fullUrl);
        var finalResult = new HandleX402Result(preSettledResponse?.Success ?? vr.IsValid, null, vr, preSettledResponse);
        context.Items[X402ResultKey] = finalResult; // Store result
        return finalResult;
    }

    private async Task<HandleX402Result> ValidatePayload(PaymentRequirements paymentRequirements, PaymentPayloadHeader payload, string fullUrl)
    {

        //If resource is included in the payload it must match the URL path
        var resource = payload.Payload.Resource;
        if (!string.IsNullOrEmpty(resource) && !string.Equals(resource, fullUrl, StringComparison.InvariantCultureIgnoreCase))
        {
            logger.LogWarning("Resource mismatch: payload {PayloadResource} vs request {RequestPath}", resource, fullUrl);
            return new HandleX402Result(false, $"Resource mismatch: payload {resource} vs request {fullUrl}");
        }

        if (payload.Scheme != paymentRequirements.Scheme)
        {
            logger.LogWarning("Scheme mismatch: payload {PayloadScheme} vs requirements {RequirementsScheme}", payload.Scheme, paymentRequirements.Scheme);
            return new HandleX402Result(false, $"Scheme mismatch: payload {payload.Scheme} vs requirements {paymentRequirements.Scheme}");
        }

        if (payload.Network != paymentRequirements.Network)
        {
            logger.LogWarning("Network mismatch: payload {PayloadNetwork} vs requirements {RequirementsNetwork}", payload.Network, paymentRequirements.Network);
            return new HandleX402Result(false, $"Network mismatch: payload {payload.Network} vs requirements {paymentRequirements.Network}");
        }

        //Check Authorization against payment requirements
        var authorization = payload.Payload.Authorization;
        if (authorization.To != paymentRequirements.PayTo)
        {
            logger.LogWarning("PayTo mismatch: authorization {AuthorizationTo} vs requirements {RequirementsPayTo}", authorization.To, paymentRequirements.PayTo);
            return new HandleX402Result(false, $"PayTo mismatch: authorization {authorization.To} vs requirements {paymentRequirements.PayTo}");
        }

        if (authorization.Value != paymentRequirements.MaxAmountRequired)
        {
            logger.LogWarning("Amount mismatch: authorization {AuthorizationValue} vs requirements {RequirementsAmount}", authorization.Value, paymentRequirements.MaxAmountRequired);
            return new HandleX402Result(false, $"Amount mismatch: authorization {authorization.Value} vs requirements {paymentRequirements.MaxAmountRequired}");
        }

        return new HandleX402Result(true);
    }

    /// <summary>
    /// Create a base64-encoded payment response header.
    /// </summary>
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

    /// <summary>
    /// Write a JSON 402 response.
    /// </summary>
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

    private Task Respond500Async(HttpContext context, string errorMsg)
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
        var tokenInfo = tokenInfoProvider.GetTokenInfo(basic.Asset);
        if (tokenInfo == null)
        {
            logger.LogWarning("No token info found for asset {Asset};", basic.Asset);
        }

        var pr = new PaymentRequirements
        {
            Scheme = basic.Scheme,
            Network = tokenInfo?.Network ?? string.Empty,
            MaxAmountRequired = basic.MaxAmountRequired,
            Asset = basic.Asset,
            MimeType = basic.MimeType,
            PayTo = basic.PayTo,
            MaxTimeoutSeconds = basic.MaxTimeoutSeconds,
            Description = basic.Description,
            Extra = new PaymentRequirementsExtra
            {
                Name = tokenInfo?.Name ?? string.Empty,
                Version = tokenInfo?.Version ?? string.Empty
            }
        };
        return pr;
    }
}
