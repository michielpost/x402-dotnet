using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using x402.Models;

namespace x402
{
    /// <summary>
    /// Middleware that enforces x402 payments on selected paths or endpoints with attributes.
    /// </summary>
    public class PaymentMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<PaymentMiddleware> logger;
        private readonly PaymentMiddlewareOptions paymentMiddlewareOptions;
        private readonly X402Handler x402Handler;

        /// <summary>
        /// Creates a payment middleware that enforces X-402 payments on configured paths or endpoints.
        /// </summary>
        /// <param name="next"></param>
        /// <param name="paymentMiddlewareOptions">Configuration options</param>
        /// <exception cref="ArgumentNullException"></exception>
        public PaymentMiddleware(RequestDelegate next,
            ILogger<PaymentMiddleware> logger,
            X402Handler x402Handler,
            PaymentMiddlewareOptions paymentMiddlewareOptions)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            this.logger = logger;
            this.paymentMiddlewareOptions = paymentMiddlewareOptions;
            this.x402Handler = x402Handler;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            string protectedPath = (context.Request.Path.Value ?? string.Empty).ToLowerInvariant();
            string protectedPathAndQuery = (context.Request.Path.Value + context.Request.QueryString.Value).ToLowerInvariant();
            var resourceFullUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
            logger.LogDebug("PaymentMiddleware invoked for path {Path}", resourceFullUrl);

            var paymentConfig = paymentMiddlewareOptions.PaymentRequirements
                .Where(x => x.Key == protectedPath && !x.Value.EnableQueryStringMatching
                            || x.Key == protectedPathAndQuery && x.Value.EnableQueryStringMatching)
                    .Select(x => x.Value)
                .FirstOrDefault();

            if (paymentConfig == null)
            {
                logger.LogDebug("No payment required for path {Path}; continuing pipeline", resourceFullUrl);
                await _next(context);
                return;
            }

            logger.LogInformation("Enforcing x402 payment for path {Path} with scheme {Scheme} asset {Asset}", resourceFullUrl, paymentConfig.PaymentRequirements.Scheme, paymentConfig.PaymentRequirements.Asset);

            var x402Result = await x402Handler.HandleX402Async(paymentConfig.PaymentRequirements, paymentConfig.PaymentRequirements.Discoverable, settlementMode: paymentMiddlewareOptions.SettlementMode).ConfigureAwait(false);
            if (!x402Result.CanContinueRequest)
            {
                logger.LogWarning("Payment not satisfied for path {Path}; responding with 402/500 already handled", resourceFullUrl);
                return;
            }

            //Payment verified, continue to next middleware
            logger.LogDebug("Payment verified for path {Path}; continuing to next middleware", resourceFullUrl);
            await _next(context);
        }

    }
}
