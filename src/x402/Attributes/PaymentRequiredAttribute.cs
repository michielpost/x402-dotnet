using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using x402.Core.Enums;
using x402.Core.Interfaces;
using x402.Core.Models.v1;

namespace x402.Attributes
{
    /// <summary>
    /// Attribute to specify payment requirements on controller actions or endpoints.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class PaymentRequiredAttribute : ActionFilterAttribute
    {
        /// <summary>
        /// The payment scheme (e.g., "exact").
        /// </summary>
        public PaymentScheme Scheme { get; set; }

        /// <summary>
        /// The maximum amount required in atomic units.
        /// </summary>
        public string MaxAmountRequired { get; set; }

        /// <summary>
        /// The asset symbol (e.g., "USDC").
        /// </summary>
        public string Asset { get; set; }

        /// <summary>
        /// The pay-to wallet address.
        /// </summary>
        public string PayTo { get; set; }

        public string Description { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;

        public SettlementMode SettlementMode { get; set; } = SettlementMode.Optimistic;

        /// <summary>
        /// List endpoint with facilitator discovery service when set to true.
        /// </summary>
        public bool Discoverable { get; set; }

        /// <summary>
        /// Creates a payment required attribute with the specified price.
        /// </summary>
        /// <param name="price">Payment amount in atomic units as string.</param>
        public PaymentRequiredAttribute(string maxAmountRequired,
            string asset,
            string payTo,
            PaymentScheme scheme = PaymentScheme.Exact)
        {
            MaxAmountRequired = maxAmountRequired;
            Asset = asset;
            PayTo = payTo;
            Scheme = scheme;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<PaymentRequiredAttribute>>();
            var x402Handler = context.HttpContext.RequestServices.GetRequiredService<X402Handler>();
            var assetInfoProvider = context.HttpContext.RequestServices.GetRequiredService<IAssetInfoProvider>();

            var request = context.HttpContext.Request;
            var fullUrl = $"{request.Scheme}://{request.Host}{request.Path}{request.QueryString}";

            logger.LogDebug("PaymentRequiredAttribute invoked for path {Path}", fullUrl);

            var assetInfo = assetInfoProvider.GetAssetInfo(this.Asset);
            if (assetInfo == null)
            {
                logger.LogWarning("No asset info found for asset {Asset};", this.Asset);
            }

            var paymentRequirements = new PaymentRequirements
            {
                Asset = this.Asset,
                Description = this.Description,
                MaxAmountRequired = this.MaxAmountRequired.ToString(),
                MimeType = this.MimeType,
                Network = assetInfo?.Network ?? string.Empty,
                PayTo = this.PayTo,
                Resource = fullUrl,
                Scheme = this.Scheme,
                MaxTimeoutSeconds = 60,
                Extra = new PaymentRequirementsExtra
                {
                    Name = assetInfo?.Name ?? string.Empty,
                    Version = assetInfo?.Version ?? string.Empty
                }
            };
            logger.LogInformation("Built payment requirements for path {Path}: scheme {Scheme}, asset {Asset}", fullUrl, paymentRequirements.Scheme, paymentRequirements.Asset);

            var x402Result = await x402Handler.HandleX402Async(paymentRequirements, Discoverable, settlementMode: SettlementMode).ConfigureAwait(false);
            if (!x402Result.CanContinueRequest)
            {
                logger.LogWarning("Payment not satisfied for path {Path}; stopping execution", fullUrl);
                return;
            }

            logger.LogDebug("Payment verified for path {Path}; executing next action", fullUrl);
            await next();
        }


    }
}
