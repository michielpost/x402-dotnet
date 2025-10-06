using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using x402.Facilitator;
using x402.Facilitator.Models;
using x402.Models;
using x402.Models.Responses;

namespace x402
{
    public class X402Handler
    {
        private static JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // default camelCase
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public static async Task<bool> HandleX402(HttpContext context, IFacilitatorClient facilitator, string path, PaymentRequirements paymentRequirements)
        {
            string? header = context.Request.Headers["X-PAYMENT"].FirstOrDefault();

            //No payment, return 402
            if (string.IsNullOrEmpty(header))
            {
                await Respond402Async(context, paymentRequirements, null);
                return false;
            }

            //Handle payment verification
            PaymentPayloadHeader? payload = null;
            VerificationResponse vr;
            try
            {
                payload = PaymentPayloadHeader.FromHeader(header);

                // If resource is included in the payload it must match the URL path
                if (payload.Payload.ContainsKey("resource"))
                {
                    var resource = payload.Payload["resource"]?.ToString();

                    if (!string.Equals(resource, path, StringComparison.Ordinal))
                    {
                        await Respond402Async(context, paymentRequirements, "resource mismatch");
                        return false;
                    }
                }

                vr = await facilitator.VerifyAsync(payload, paymentRequirements);
            }
            catch (ArgumentException)
            {
                // Malformed payment header - client error
                await Respond402Async(context, paymentRequirements, "Malformed X-PAYMENT header");
                return false;
            }
            catch (IOException ex)
            {
                // Network/communication error with facilitator - server error
                await Respond500Async(context, "Payment verification failed: " + ex.Message);
                return false;
            }
            catch (Exception)
            {
                // Other unexpected errors - server error
                await Respond500Async(context, "Internal server error during payment verification");
                return false;
            }

            if (!vr.IsValid)
            {
                await Respond402Async(context, paymentRequirements, vr.InvalidReason);
                return false;
            }

            //Settlement writes a header, it must be written before any other content
            //context.Response.OnStarting(async () =>
            //{
            //    //Start settlement
            //    try
            //    {
            //        SettlementResponse sr = await facilitator.SettleAsync(payload, paymentRequirements);
            //        if (sr == null || !sr.Success)
            //        {
            //            // Settlement failed - return 402 if headers not sent yet
            //            if (!context.Response.HasStarted)
            //            {
            //                string errorMsg = sr != null && sr.ErrorReason != null ? sr.ErrorReason : FacilitatorErrorCodes.UnexpectedSettleError;
            //                await Respond402Async(context, paymentRequirements, errorMsg);
            //            }
            //            return;
            //        }

            //        // Settlement succeeded - add settlement response header (base64-encoded JSON)
            //        try
            //        {
            //            // Extract payer from payment payload (wallet address of person making payment)
            //            string? payer = payload.ExtractPayerFromPayload();

            //            string base64Header = CreatePaymentResponseHeader(sr, payer);
            //            context.Response.Headers.Append("X-PAYMENT-RESPONSE", base64Header);

            //            // Set CORS header to expose X-PAYMENT-RESPONSE to browser clients
            //            context.Response.Headers.Append("Access-Control-Expose-Headers", "X-PAYMENT-RESPONSE");
            //        }
            //        catch (Exception)
            //        {
            //            // If header creation fails, return 500
            //            if (!context.Response.HasStarted)
            //            {
            //                await Respond500Async(context, "Failed to create settlement response header");
            //            }
            //            return;
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        // Network/communication errors during settlement - return 402
            //        if (!context.Response.HasStarted)
            //        {
            //            await Respond402Async(context, paymentRequirements, "settlement error: " + ex.Message);
            //        }
            //        return;
            //    }
            //});

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
        private static async Task Respond402Async(HttpContext context, PaymentRequirements paymentRequirements, string? error)
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
            await context.Response.WriteAsync(json);
        }

        private static async Task Respond500Async(HttpContext context, string errorMsg)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            string json = "{\"error\":\"" + errorMsg + "\"}";
            await context.Response.WriteAsync(json);
        }
    }
}
