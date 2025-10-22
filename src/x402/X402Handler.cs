using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using x402.Core.Enums;
using x402.Core.Models;
using x402.Core.Models.Facilitator;
using x402.Core.Models.Responses;
using x402.Facilitator;

namespace x402
{
    public class X402Handler
    {
        private static readonly JsonSerializerOptions jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // default camelCase
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public static async Task<HandleX402Result> HandleX402Async(
            HttpContext context,
            IFacilitatorClient facilitator,
            PaymentRequirements paymentRequirements,
            bool discoverable,
            SettlementMode settlementMode = SettlementMode.Optimistic,
            Func<HttpContext, SettlementResponse, Task>? onSettlement = null,
            Func<HttpContext, PaymentRequirements, OutputSchema, OutputSchema>? onSetOutputSchema = null)
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<X402Handler>>();

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
                    logger.LogError(ex, "onSettlement callback threw for path {Path}", fullUrl);
                }
            }
            paymentRequirements.OutputSchema = outputSchema;


            //No payment, return 402
            if (string.IsNullOrEmpty(header))
            {
                logger.LogInformation("No X-PAYMENT header present for path {Path}; responding 402", fullUrl);
                await Respond402Async(context, paymentRequirements, "X-PAYMENT header is required").ConfigureAwait(false);
                return new HandleX402Result(false);
            }

            //Handle payment verification
            PaymentPayloadHeader? payload = null;
            VerificationResponse? vr = null;
            try
            {
                payload = PaymentPayloadHeader.FromHeader(header);
                logger.LogDebug("Parsed X-PAYMENT header for path {Path}", fullUrl);

                //If resource is included in the payload it must match the URL path
                var resource = payload.Payload.Resource;

                if (!string.IsNullOrEmpty(resource) && !string.Equals(resource, fullUrl, StringComparison.Ordinal))
                {
                    logger.LogWarning("Resource mismatch: payload {PayloadResource} vs request {RequestPath}", resource, fullUrl);
                    await Respond402Async(context, paymentRequirements, "resource mismatch").ConfigureAwait(false);
                    return new HandleX402Result(false, $"Resource mismatch: payload {resource} vs request {fullUrl}");
                }

                vr = await facilitator.VerifyAsync(payload, paymentRequirements).ConfigureAwait(false);
                logger.LogInformation("Verification completed for path {Path}: IsValid={IsValid}", fullUrl, vr.IsValid);
            }
            catch (ArgumentException)
            {
                // Malformed payment header - client error
                logger.LogWarning("Malformed X-PAYMENT header for path {Path}", fullUrl);
                await Respond402Async(context, paymentRequirements, "Malformed X-PAYMENT header").ConfigureAwait(false);
                return new HandleX402Result(false, "Malformed X-PAYMENT header", vr);
            }
            catch (IOException ex)
            {
                // Network/communication error with facilitator - server error
                logger.LogError(ex, "Payment verification IO error for path {Path}", fullUrl);
                await Respond500Async(context, "Payment verification failed: " + ex.Message).ConfigureAwait(false);
                return new HandleX402Result(false, $"Payment verification failed: {ex.Message}", vr);
            }
            catch (Exception ex)
            {
                // Other unexpected errors - server error
                logger.LogError(ex, "Unexpected error during payment verification for path {Path}", fullUrl);
                await Respond500Async(context, "Internal server error during payment verification").ConfigureAwait(false);
                return new HandleX402Result(false, $"Internal server error during payment verification", vr);
            }

            if (!vr.IsValid)
            {
                logger.LogInformation("Verification invalid for path {Path}: {Reason}", fullUrl, vr.InvalidReason);
                await Respond402Async(context, paymentRequirements, vr.InvalidReason).ConfigureAwait(false);
                return new HandleX402Result(false, vr.InvalidReason, vr);
            }

            // Optional pessimistic settlement in the main flow
            SettlementResponse? preSettledResponse = null;
            if (settlementMode == SettlementMode.Pessimistic)
            {
                try
                {
                    preSettledResponse = await facilitator.SettleAsync(payload, paymentRequirements).ConfigureAwait(false);
                    if (preSettledResponse == null || !preSettledResponse.Success)
                    {
                        string errorMsg = preSettledResponse != null && preSettledResponse.ErrorReason != null
                            ? preSettledResponse.ErrorReason
                            : FacilitatorErrorCodes.UnexpectedSettleError;
                        logger.LogWarning("Pessimistic settlement failed for path {Path}: {Reason}", fullUrl, errorMsg);
                        await Respond402Async(context, paymentRequirements, errorMsg).ConfigureAwait(false);
                        return new HandleX402Result(false, errorMsg, vr);
                    }

                    // Invoke callback early when available
                    if (onSettlement != null)
                    {
                        try
                        {
                            await onSettlement(context, preSettledResponse).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "onSettlement callback threw for path {Path}", fullUrl);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Pessimistic settlement error for path {Path}", fullUrl);
                    await Respond402Async(context, paymentRequirements, "settlement error: " + ex.Message).ConfigureAwait(false);
                    return new HandleX402Result(false, "settlement error: " + ex.Message, vr);
                }
            }

            // Settlement header must be written before any other content
            context.Response.OnStarting(async () =>
            {
                //const string onStartingGuardKey = "__x402_onstarting_executed";
                //if (context.Items.ContainsKey(onStartingGuardKey))
                //{
                //    return;
                //}
                //context.Items[onStartingGuardKey] = true;

                //Start settlement
                try
                {
                    SettlementResponse sr = preSettledResponse ?? await facilitator.SettleAsync(payload, paymentRequirements).ConfigureAwait(false);
                    if (sr == null || !sr.Success)
                    {
                        // Settlement failed
                        string errorMsg = sr != null && sr.ErrorReason != null ? sr.ErrorReason : FacilitatorErrorCodes.UnexpectedSettleError;
                        logger.LogWarning("Settlement failed for path {Path}: {Reason}", fullUrl, errorMsg);

                        // In pessimistic mode, downgrade to 402 if possible (headers not sent)
                        // In optimistic mode, do not alter the response status/body
                        if (settlementMode == SettlementMode.Pessimistic && !context.Response.HasStarted)
                        {
                            await Respond402Async(context, paymentRequirements, errorMsg).ConfigureAwait(false);
                        }
                        return;
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

                        // Invoke callback when settlement completes (optimistic path)
                        if (preSettledResponse == null && onSettlement != null)
                        {
                            try
                            {
                                await onSettlement(context, sr).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "onSettlement callback threw for path {Path}", fullUrl);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // If header creation fails
                        if (settlementMode == SettlementMode.Pessimistic && !context.Response.HasStarted)
                        {
                            logger.LogError("Failed to create settlement response header for path {Path}", fullUrl);
                            await Respond500Async(context, "Failed to create settlement response header").ConfigureAwait(false);
                        }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // Network/communication errors during settlement
                    if (settlementMode == SettlementMode.Pessimistic && !context.Response.HasStarted)
                    {
                        logger.LogError(ex, "Settlement error for path {Path}", fullUrl);
                        await Respond402Async(context, paymentRequirements, "settlement error: " + ex.Message).ConfigureAwait(false);
                    }
                    return;
                }
            });

            logger.LogDebug("Payment verified; proceeding to response for path {Path}", fullUrl);
            return new HandleX402Result(preSettledResponse?.Success ?? true, null, vr);
        }

        /// <summary>
        /// Create a base64-encoded payment response header.
        /// </summary>
        private static string CreatePaymentResponseHeader(SettlementResponse sr, string? payer)
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
        private static Task Respond402Async(HttpContext context, PaymentRequirements paymentRequirements, string? error)
        {
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

        private static Task Respond500Async(HttpContext context, string errorMsg)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            string json = "{\"error\":\"" + errorMsg + "\"}";
            return context.Response.WriteAsync(json);
        }
    }
}
