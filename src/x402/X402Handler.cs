using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
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

        public static async Task<bool> HandleX402Async(HttpContext context, IFacilitatorClient facilitator, string path, PaymentRequirements paymentRequirements)
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<X402Handler>>();
            logger.LogDebug("HandleX402 invoked for path {Path}", path);
            string? header = context.Request.Headers["X-PAYMENT"].FirstOrDefault();

            //No payment, return 402
            if (string.IsNullOrEmpty(header))
            {
                logger.LogInformation("No X-PAYMENT header present for path {Path}; responding 402", path);
                await Respond402Async(context, paymentRequirements, null).ConfigureAwait(false);
                return false;
            }

            //Handle payment verification
            PaymentPayloadHeader? payload = null;
            VerificationResponse vr;
            try
            {
                payload = PaymentPayloadHeader.FromHeader(header);
                logger.LogDebug("Parsed X-PAYMENT header for path {Path}", path);

                // If resource is included in the payload it must match the URL path
                if (payload.Payload.TryGetValue("resource", out object? value))
                {
                    var resource = value?.ToString();

                    if (!string.Equals(resource, path, StringComparison.Ordinal))
                    {
                        logger.LogWarning("Resource mismatch: payload {PayloadResource} vs request {RequestPath}", resource, path);
                        await Respond402Async(context, paymentRequirements, "resource mismatch").ConfigureAwait(false);
                        return false;
                    }
                }

                vr = await facilitator.VerifyAsync(payload, paymentRequirements).ConfigureAwait(false);
                logger.LogInformation("Verification completed for path {Path}: IsValid={IsValid}", path, vr.IsValid);
            }
            catch (ArgumentException)
            {
                // Malformed payment header - client error
                logger.LogWarning("Malformed X-PAYMENT header for path {Path}", path);
                await Respond402Async(context, paymentRequirements, "Malformed X-PAYMENT header").ConfigureAwait(false);
                return false;
            }
            catch (IOException ex)
            {
                // Network/communication error with facilitator - server error
                logger.LogError(ex, "Payment verification IO error for path {Path}", path);
                await Respond500Async(context, "Payment verification failed: " + ex.Message).ConfigureAwait(false);
                return false;
            }
            catch (Exception)
            {
                // Other unexpected errors - server error
                logger.LogError("Unexpected error during payment verification for path {Path}", path);
                await Respond500Async(context, "Internal server error during payment verification").ConfigureAwait(false);
                return false;
            }

            if (!vr.IsValid)
            {
                logger.LogInformation("Verification invalid for path {Path}: {Reason}", path, vr.InvalidReason);
                await Respond402Async(context, paymentRequirements, vr.InvalidReason).ConfigureAwait(false);
                return false;
            }

            //Settlement writes a header, it must be written before any other content
            context.Response.OnStarting(async () =>
            {
                //Start settlement
                try
                {
                    SettlementResponse sr = await facilitator.SettleAsync(payload, paymentRequirements).ConfigureAwait(false);
                    if (sr == null || !sr.Success)
                    {
                        // Settlement failed - return 402 if headers not sent yet
                        if (!context.Response.HasStarted)
                        {
                            string errorMsg = sr != null && sr.ErrorReason != null ? sr.ErrorReason : FacilitatorErrorCodes.UnexpectedSettleError;
                            logger.LogWarning("Settlement failed for path {Path}: {Reason}", path, errorMsg);
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
                        logger.LogInformation("Settlement succeeded for path {Path}; response header appended (payer={Payer})", path, payer);
                    }
                    catch (Exception)
                    {
                        // If header creation fails, return 500
                        if (!context.Response.HasStarted)
                        {
                            logger.LogError("Failed to create settlement response header for path {Path}", path);
                            await Respond500Async(context, "Failed to create settlement response header").ConfigureAwait(false);
                        }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // Network/communication errors during settlement - return 402
                    if (!context.Response.HasStarted)
                    {
                        logger.LogError(ex, "Settlement error for path {Path}", path);
                        await Respond402Async(context, paymentRequirements, "settlement error: " + ex.Message).ConfigureAwait(false);
                    }
                    return;
                }
            });

            logger.LogDebug("Payment verified; proceeding to response for path {Path}", path);
            return true;
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
