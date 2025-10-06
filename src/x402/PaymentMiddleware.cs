using Microsoft.AspNetCore.Http;
using System.Text;
using System.Text.Json;
using x402.Facilitator;
using x402.Facilitator.Models;
using x402.Models;
using x402.Models.Responses;

namespace x402
{
    /// <summary>
    /// Middleware that enforces x402 payments on selected paths or endpoints with attributes.
    /// </summary>
    public class PaymentMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly PaymentMiddlewareOptions paymentMiddlewareOptions;
        private readonly IFacilitatorClient facilitator;

        private JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // default camelCase
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Creates a payment middleware that enforces X-402 payments on configured paths or endpoints.
        /// </summary>
        /// <param name="next"></param>
        /// <param name="paymentMiddlewareOptions">Configuration options</param>
        /// <exception cref="ArgumentNullException"></exception>
        public PaymentMiddleware(RequestDelegate next,
            PaymentMiddlewareOptions paymentMiddlewareOptions)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            this.paymentMiddlewareOptions = paymentMiddlewareOptions;
            this.facilitator = paymentMiddlewareOptions.Facilitator;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            string path = context.Request.Path.Value + context.Request.QueryString.Value;

            PaymentRequirementsConfig? paymentRequirements = null;

            //var endpoint = context.GetEndpoint();
            //if (endpoint != null)
            //{
            //    var attr = endpoint.Metadata.GetMetadata<PaymentRequiredAttribute>();
            //    if (attr != null)
            //    {
            //        priceStr = attr.Price;
            //        scheme = attr.Scheme ?? "exact";
            //    }
            //}

            if (paymentRequirements == null && (paymentMiddlewareOptions.PaymentRequirements?.TryGetValue(path, out var tablePrice) ?? false))
            {
                paymentRequirements = tablePrice;
            }

            if (paymentRequirements == null)
            {
                await _next(context);
                return;
            }

            string? header = context.Request.Headers["X-PAYMENT"].FirstOrDefault();

            //No payment, return 402
            if (string.IsNullOrEmpty(header))
            {
                await Respond402Async(context, path, paymentRequirements, null);
                return;
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
                        await Respond402Async(context, path, paymentRequirements, "resource mismatch");
                        return;
                    }
                }

                vr = await facilitator.VerifyAsync(payload, BuildRequirements(path, paymentRequirements));
            }
            catch (ArgumentException)
            {
                // Malformed payment header - client error
                await Respond402Async(context, path, paymentRequirements, "Malformed X-PAYMENT header");
                return;
            }
            catch (IOException ex)
            {
                // Network/communication error with facilitator - server error
                await Respond500Async(context, "Payment verification failed: " + ex.Message);
                return;
            }
            catch (Exception)
            {
                // Other unexpected errors - server error
                await Respond500Async(context, "Internal server error during payment verification");
                return;
            }

            if (!vr.IsValid)
            {
                await Respond402Async(context, path, paymentRequirements, vr.InvalidReason);
                return;
            }

            //Settlement writes a header, it must be written before any other content
            context.Response.OnStarting(async () =>
            {
                //Start settlement
                try
                {
                    SettlementResponse sr = await facilitator.SettleAsync(payload, BuildRequirements(path, paymentRequirements));
                    if (sr == null || !sr.Success)
                    {
                        // Settlement failed - return 402 if headers not sent yet
                        if (!context.Response.HasStarted)
                        {
                            string errorMsg = sr != null && sr.ErrorReason != null ? sr.ErrorReason : FacilitatorErrorCodes.UnexpectedSettleError;
                            await Respond402Async(context, path, paymentRequirements, errorMsg);
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
                    }
                    catch (Exception)
                    {
                        // If header creation fails, return 500
                        if (!context.Response.HasStarted)
                        {
                            await Respond500Async(context, "Failed to create settlement response header");
                        }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // Network/communication errors during settlement - return 402
                    if (!context.Response.HasStarted)
                    {
                        await Respond402Async(context, path, paymentRequirements, "settlement error: " + ex.Message);
                    }
                    return;
                }
            });


            //Payment verified, continue to next middleware
            await _next(context);
        }

        /// <summary>
        /// Build a PaymentRequirements object for the given path, scheme, and price.
        /// </summary>
        private PaymentRequirements BuildRequirements(string path, PaymentRequirementsConfig paymentRequirements)
        {
            var pr = new PaymentRequirements
            {
                Scheme = paymentRequirements.Scheme,
                Network = paymentRequirements.Network ?? paymentMiddlewareOptions.DefaultNetwork ?? throw new ArgumentNullException(nameof(paymentRequirements.Network)),
                MaxAmountRequired = paymentRequirements.MaxAmountRequired.ToString(),
                Asset = paymentRequirements.Asset,
                Resource = path,
                MimeType = paymentRequirements.MimeType,
                PayTo = paymentRequirements.PayTo ?? paymentMiddlewareOptions.DefaultPayToAddress ?? throw new ArgumentNullException(nameof(paymentRequirements.PayTo)),
                MaxTimeoutSeconds = 30,
                Description = paymentRequirements.Description
            };
            return pr;
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
        private async Task Respond402Async(HttpContext context, string path, PaymentRequirementsConfig paymentRequirements, string? error)
        {
            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            context.Response.ContentType = "application/json";

            var prr = new PaymentRequiredResponse
            {
                X402Version = 1,
                Accepts = new List<PaymentRequirements> { BuildRequirements(path, paymentRequirements) },
                Error = error
            };

            string json = JsonSerializer.Serialize(prr, jsonOptions);
            await context.Response.WriteAsync(json);
        }

        private async Task Respond500Async(HttpContext context, string errorMsg)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            string json = "{\"error\":\"" + errorMsg + "\"}";
            await context.Response.WriteAsync(json);
        }
    }
}
