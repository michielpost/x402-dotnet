using Microsoft.AspNetCore.Http;
using x402.Facilitator;
using x402.Models;

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

            PaymentRequirements? paymentRequirements = null;

            if (paymentRequirements == null && (paymentMiddlewareOptions.PaymentRequirements?.TryGetValue(path, out var tablePrice) ?? false))
            {
                paymentRequirements = BuildRequirements(path, tablePrice, paymentMiddlewareOptions.DefaultNetwork, paymentMiddlewareOptions.DefaultPayToAddress);
            }

            if (paymentRequirements == null)
            {
                await _next(context);
                return;
            }

            bool canContinue = await X402Handler.HandleX402(context, facilitator, path, paymentRequirements);
            if (!canContinue)
            {
                return;
            }

            //Payment verified, continue to next middleware
            await _next(context);
        }

        /// <summary>
        /// Build a PaymentRequirements object for the given path, scheme, and price.
        /// </summary>
        private static PaymentRequirements BuildRequirements(string path, PaymentRequirementsConfig paymentRequirements, string? defaultNetwork, string? defaultPayToAddress)
        {
            var pr = new PaymentRequirements
            {
                Scheme = paymentRequirements.Scheme,
                Network = paymentRequirements.Network ?? defaultNetwork ?? throw new ArgumentNullException(nameof(paymentRequirements.Network)),
                MaxAmountRequired = paymentRequirements.MaxAmountRequired.ToString(),
                Asset = paymentRequirements.Asset,
                Resource = path,
                MimeType = paymentRequirements.MimeType,
                PayTo = paymentRequirements.PayTo ?? defaultPayToAddress ?? throw new ArgumentNullException(nameof(paymentRequirements.PayTo)),
                MaxTimeoutSeconds = 30,
                Description = paymentRequirements.Description
            };
            return pr;
        }

    }
}
