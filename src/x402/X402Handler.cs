using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using x402.Enums;
using x402.Facilitator;
using x402.Facilitator.Models;
using x402.Models;
using x402.Models.Responses;

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
            string path,
            PaymentRequirements paymentRequirements,
            SettlementMode settlementMode = SettlementMode.Optimistic,
            Func<HttpContext, SettlementResponse, Task>? onSettlement = null)
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<X402Handler>>();
            logger.LogDebug("HandleX402 invoked for path {Path}", path);

            if (facilitator is CorbitsFacilitatorClient corbitsFacilitator)
            {
                try
                {
                    var updatedRequirements = await corbitsFacilitator.AcceptsAsync(new List<PaymentRequirements> { paymentRequirements }).ConfigureAwait(false);
                    var matchingRequirement = FindMatchingPaymentRequirements(updatedRequirements, paymentRequirements, logger);

                    if (matchingRequirement != null)
                    {
                        paymentRequirements = matchingRequirement;
                        logger.LogInformation("Updated payment requirements from facilitator /accepts");
                    }
                    else if (updatedRequirements.Count > 0)
                    {
                        logger.LogWarning("No exact match found from facilitator /accepts, using original requirements");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to get updated requirements from facilitator /accepts");
                }
            }

            string? header = context.Request.Headers["X-PAYMENT"].FirstOrDefault();

            //No payment, return 402
            if (string.IsNullOrEmpty(header))
            {
                logger.LogInformation("No X-PAYMENT header present for path {Path}; responding 402", path);
                await Respond402Async(context, facilitator, paymentRequirements, "X-PAYMENT header is required").ConfigureAwait(false);
                return new HandleX402Result(false);
            }

            //Handle payment verification
            PaymentPayloadHeader? payload = null;
            VerificationResponse? vr = null;
            try
            {
                payload = PaymentPayloadHeader.FromHeader(header);
                logger.LogDebug("Parsed X-PAYMENT header for path {Path}", path);

                //If resource is included in the payload it must match the URL path
                var resource = payload.Payload.Resource;

                if (!string.IsNullOrEmpty(resource) && !string.Equals(resource, path, StringComparison.Ordinal))
                {
                    logger.LogWarning("Resource mismatch: payload {PayloadResource} vs request {RequestPath}", resource, path);
                    await Respond402Async(context, facilitator, paymentRequirements, "resource mismatch").ConfigureAwait(false);
                    return new HandleX402Result(false, $"Resource mismatch: payload {resource} vs request {path}");
                }

                vr = await facilitator.VerifyAsync(payload, paymentRequirements).ConfigureAwait(false);
                logger.LogInformation("Verification completed for path {Path}: IsValid={IsValid}", path, vr.IsValid);
            }
            catch (ArgumentException)
            {
                // Malformed payment header - client error
                logger.LogWarning("Malformed X-PAYMENT header for path {Path}", path);
                await Respond402Async(context, facilitator, paymentRequirements, "Malformed X-PAYMENT header").ConfigureAwait(false);
                return new HandleX402Result(false, "Malformed X-PAYMENT header", vr);
            }
            catch (IOException ex)
            {
                // Network/communication error with facilitator - server error
                logger.LogError(ex, "Payment verification IO error for path {Path}", path);
                await Respond500Async(context, "Payment verification failed: " + ex.Message).ConfigureAwait(false);
                return new HandleX402Result(false, $"Payment verification failed: {ex.Message}", vr);
            }
            catch (Exception ex)
            {
                // Other unexpected errors - server error
                logger.LogError(ex, "Unexpected error during payment verification for path {Path}", path);
                await Respond500Async(context, "Internal server error during payment verification").ConfigureAwait(false);
                return new HandleX402Result(false, $"Internal server error during payment verification", vr);
            }

            if (!vr.IsValid)
            {
                logger.LogInformation("Verification invalid for path {Path}: {Reason}", path, vr.InvalidReason);
                await Respond402Async(context, facilitator, paymentRequirements, vr.InvalidReason).ConfigureAwait(false);
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
                        logger.LogWarning("Pessimistic settlement failed for path {Path}: {Reason}", path, errorMsg);
                        await Respond402Async(context, facilitator, paymentRequirements, errorMsg).ConfigureAwait(false);
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
                            logger.LogError(ex, "onSettlement callback threw for path {Path}", path);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Pessimistic settlement error for path {Path}", path);
                    await Respond402Async(context, facilitator, paymentRequirements, "settlement error: " + ex.Message).ConfigureAwait(false);
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
                        logger.LogWarning("Settlement failed for path {Path}: {Reason}", path, errorMsg);

                        // In pessimistic mode, downgrade to 402 if possible (headers not sent)
                        // In optimistic mode, do not alter the response status/body
                        if (settlementMode == SettlementMode.Pessimistic && !context.Response.HasStarted)
                        {
                            await Respond402Async(context, facilitator, paymentRequirements, errorMsg).ConfigureAwait(false);
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
                        logger.LogInformation("Settlement succeeded for path {Path}; response header appended (payer={Payer})", path, payer);

                        // Invoke callback when settlement completes (optimistic path)
                        if (preSettledResponse == null && onSettlement != null)
                        {
                            try
                            {
                                await onSettlement(context, sr).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "onSettlement callback threw for path {Path}", path);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // If header creation fails
                        if (settlementMode == SettlementMode.Pessimistic && !context.Response.HasStarted)
                        {
                            logger.LogError("Failed to create settlement response header for path {Path}", path);
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
                        logger.LogError(ex, "Settlement error for path {Path}", path);
                        await Respond402Async(context, facilitator, paymentRequirements, "settlement error: " + ex.Message).ConfigureAwait(false);
                    }
                    return;
                }
            });

            logger.LogDebug("Payment verified; proceeding to response for path {Path}", path);
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
        private static async Task Respond402Async(HttpContext context, IFacilitatorClient facilitator, PaymentRequirements paymentRequirements, string? error)
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<X402Handler>>();

            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            context.Response.ContentType = "application/json";

            var prr = new PaymentRequiredResponse
            {
                X402Version = 1,
                Accepts = new List<PaymentRequirements> { paymentRequirements },
                Error = error
            };

            string json = JsonSerializer.Serialize(prr, jsonOptions);
            await context.Response.WriteAsync(json).ConfigureAwait(false);
        }

        /// <summary>
        /// Finds a matching payment requirement from a list based on network, scheme, and asset.
        /// </summary>
        private static PaymentRequirements? FindMatchingPaymentRequirements(
            List<PaymentRequirements> accepts,
            PaymentRequirements target,
            ILogger logger)
        {
            List<PaymentRequirements> possible;

            if (!string.IsNullOrEmpty(target.Asset))
            {
                possible = accepts.Where(x =>
                    x.Network == target.Network &&
                    x.Scheme == target.Scheme &&
                    x.Asset == target.Asset
                ).ToList();
            }
            else
            {
                possible = accepts.Where(x =>
                    x.Network == target.Network &&
                    x.Scheme == target.Scheme
                ).ToList();
            }

            if (possible.Count > 1)
            {
                logger.LogWarning("Found {Count} ambiguous matching requirements for payment", possible.Count);
            }

            return possible.FirstOrDefault();
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
