﻿using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using x402.Core.Enums;
using x402.Core.Interfaces;
using x402.Core.Models;
using x402.Core.Models.Facilitator;
using x402.Core.Models.Responses;
using x402.Core.Models.v1;
using x402.Facilitator;

namespace x402;

public class X402HandlerV1
{
    public static readonly string X402ResultKey = "X402HandleResult";
    public static readonly string PaymentHeaderV1 = "X-PAYMENT";
    public static readonly string PaymentResponseHeaderV1 = "X-PAYMENT-RESPONSE";

    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<X402HandlerV1> logger;
    private readonly IFacilitatorV1Client facilitator;
    private readonly IAssetInfoProvider assetInfoProvider;
    private readonly IHttpContextAccessor httpContextAccessor;

    public X402HandlerV1(
        ILogger<X402HandlerV1> logger,
        IFacilitatorV1Client facilitator,
        IAssetInfoProvider assetInfoProvider,
        IHttpContextAccessor httpContextAccessor)
    {
        this.logger = logger;
        this.facilitator = facilitator;
        this.assetInfoProvider = assetInfoProvider;
        this.httpContextAccessor = httpContextAccessor;
    }

    public async Task<X402ProcessingResult> HandleX402Async(
        PaymentRequiredInfo paymentRequiredInfo,
        SettlementMode settlementMode = SettlementMode.Pessimistic,
        Func<HttpContext, SettlementResponse?, Exception?, Task>? onSettlement = null,
        Func<HttpContext, PaymentRequirements, OutputSchema, OutputSchema>? onSetOutputSchema = null)
    {
        var paymentRequirements = paymentRequiredInfo.Accepts.Select(x => FillPaymentRequirements(x, paymentRequiredInfo.Resource)).ToList();

        var result = await HandleX402Async(paymentRequirements, paymentRequiredInfo.Discoverable, settlementMode, onSettlement, onSetOutputSchema);
        StoreResult(result);
        return result;
    }

    public async Task<X402ProcessingResult> HandleX402Async(
        List<PaymentRequirements> paymentRequirements,
        bool discoverable,
        SettlementMode settlementMode = SettlementMode.Pessimistic,
        Func<HttpContext, SettlementResponse?, Exception?, Task>? onSettlement = null,
        Func<HttpContext, PaymentRequirements, OutputSchema, OutputSchema>? onSetOutputSchema = null)
    {
        var context = GetHttpContext();
        var fullUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}".ToLowerInvariant();

        foreach (var paymentRequirementsItem in paymentRequirements)
        {
            paymentRequirementsItem.Resource = fullUrl;

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
                    outputSchema = onSetOutputSchema(context, paymentRequirementsItem, outputSchema);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "onSetOutputSchema callback threw for path {Path}", fullUrl);
                }
            }
            paymentRequirementsItem.OutputSchema = outputSchema;
        }

        logger.LogDebug("HandleX402 invoked for path {Path}", fullUrl);

        string? header = context.Request.Headers[PaymentHeaderV1].FirstOrDefault();

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
        List<PaymentRequirements> paymentRequirements,
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
            logger.LogDebug("Parsed header for path {Path}", fullUrl);

            var validationResult = await ValidatePayload(paymentRequirements, payload, fullUrl);
            if (!validationResult.CanContinueRequest || validationResult.SelectedPaymentRequirement == null)
            {
                return X402ProcessingResult.CreateError(
                    paymentRequirements,
                    validationResult.Error ?? "Validation failed",
                    StatusCodes.Status402PaymentRequired,
                    fullUrl: fullUrl);
            }

            var vr = await facilitator.VerifyAsync(payload, validationResult.SelectedPaymentRequirement);
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
                (preSettledResponse, settlementException) = await HandlePessimisticSettlement(payload, validationResult.SelectedPaymentRequirement, fullUrl);
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
                validationResult.SelectedPaymentRequirement,
                vr,
                preSettledResponse,
                payload,
                fullUrl,
                settlementMode == SettlementMode.Pessimistic);
        }
        catch (ArgumentException)
        {
            logger.LogWarning("Malformed payment header for path {Path}", fullUrl);
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
                if (settlementMode == SettlementMode.DoNotSettle)
                {
                    logger.LogInformation("Settlement skipped (DoNotSettle) for path {Path}", processingResult.FullUrl);
                    sr = new SettlementResponse
                    {
                        Success = true,
                        Transaction = null,
                        Payer = processingResult.PaymentPayload?.ExtractPayerFromPayload(),
                        Network = processingResult.SelectedPaymentRequirement?.Network
                    };
                }

                if (sr == null && processingResult.PaymentPayload != null && processingResult.SelectedPaymentRequirement != null)
                {
                    sr = await facilitator.SettleAsync(processingResult.PaymentPayload, processingResult.SelectedPaymentRequirement);
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

            context.Response.Headers.Append(PaymentResponseHeaderV1, base64Header);
            context.Response.Headers.Append("Access-Control-Expose-Headers", PaymentResponseHeaderV1);

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

    private async Task<X402ProcessingResult> ValidatePayload(List<PaymentRequirements> paymentRequirements, PaymentPayloadHeader payload, string fullUrl)
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

        var selectedRequirement = paymentRequirements.FirstOrDefault(pr =>
            pr.Scheme == payload.Scheme &&
            pr.Network == payload.Network &&
            pr.PayTo == payload.Payload.Authorization.To &&
            pr.MaxAmountRequired == payload.Payload.Authorization.Value);

        if (selectedRequirement == null)
        {
            logger.LogWarning("No matching payment requirements found for payload: Scheme={PayloadScheme}, Network={PayloadNetwork}, PayTo={AuthorizationTo}, Amount={AuthorizationValue}",
                payload.Scheme,
                payload.Network,
                payload.Payload.Authorization.To,
                payload.Payload.Authorization.Value);
            return X402ProcessingResult.CreateError(
                paymentRequirements,
                "No matching payment requirements found for the provided payload",
                StatusCodes.Status402PaymentRequired,
                fullUrl: fullUrl);

        }

        var authorization = payload.Payload.Authorization;
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
        if (!hasValidAfter || validAfter > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            logger.LogWarning("Authorization not yet valid: validAfter {ValidAfter} is in the future", authorization.ValidAfter);
            return X402ProcessingResult.CreateError(
                paymentRequirements,
                $"Authorization not yet valid: validAfter {authorization.ValidAfter} is in the future",
                StatusCodes.Status402PaymentRequired,
                fullUrl: fullUrl);
        }


        return X402ProcessingResult.Success(paymentRequirements, selectedRequirement, null!, fullUrl: fullUrl);
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

    private Task Respond402Async(HttpContext context, List<PaymentRequirements> paymentRequirements, string? error)
    {
        if (context.Response.HasStarted)
        {
            logger.LogWarning("Response already started; cannot write");
            return Task.CompletedTask;
        }

        var prr = new PaymentRequiredResponse
        {
            X402Version = 1,
            Accepts = paymentRequirements,
            Error = error
        };

        string json = JsonSerializer.Serialize(prr, jsonOptions);

        context.Response.StatusCode = StatusCodes.Status402PaymentRequired;

        context.Response.ContentType = "application/json";
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

    private PaymentRequirements FillPaymentRequirements(PaymentRequirementsBasic basic, ResourceInfoBasic? resourceInfoBasic)
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
            MaxAmountRequired = basic.Amount,
            Asset = basic.Asset,
            MimeType = resourceInfoBasic?.MimeType ?? string.Empty,
            PayTo = basic.PayTo,
            MaxTimeoutSeconds = basic.MaxTimeoutSeconds,
            Description = resourceInfoBasic?.Description ?? string.Empty,
            Extra = new PaymentRequirementsExtra
            {
                Name = assetInfo?.Name ?? string.Empty,
                Version = assetInfo?.Version ?? string.Empty
            }
        };
    }
}