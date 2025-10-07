using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using x402.Enums;
using x402.Facilitator;
using x402.Models;

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
        /// The network identifier (e.g., "base-sepolia").
        /// </summary>
        public string Network { get; set; }

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

        /// <summary>
        /// Creates a payment required attribute with the specified price.
        /// </summary>
        /// <param name="price">Payment amount in atomic units as string.</param>
        public PaymentRequiredAttribute(string maxAmountRequired,
            string asset,
            string payTo,
            string network,
            PaymentScheme scheme = PaymentScheme.Exact)
        {
            MaxAmountRequired = maxAmountRequired;
            Asset = asset;
            PayTo = payTo;
            Network = network;
            Scheme = scheme;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<PaymentRequiredAttribute>>();
            var facilitator = context.HttpContext.RequestServices.GetRequiredService<IFacilitatorClient>();
           
            var request = context.HttpContext.Request;
            string path = request.Path.Value + request.QueryString.Value;

            PaymentRequirements? paymentRequirements = new PaymentRequirements
            {
                Asset = this.Asset,
                Description = this.Description,
                MaxAmountRequired = this.MaxAmountRequired.ToString(),
                MimeType = this.MimeType,
                Network = this.Network,
                PayTo = this.PayTo,
                Resource = path,
                Scheme = this.Scheme,
                MaxTimeoutSeconds = 30
            };

            bool canContinue = await X402Handler.HandleX402(context.HttpContext, facilitator, path, paymentRequirements);
            if (!canContinue)
            {
                return;
            }

            await next();
        }


    }
}
