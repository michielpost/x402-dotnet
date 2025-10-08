using System.Numerics;
using x402.Enums;

namespace x402.Models
{
    /// <summary>
    /// Represents the requirements for a payment.
    /// </summary>
    public class PaymentRequirementsConfig
    {
        /// <summary>
        /// The payment scheme (e.g., "exact").
        /// </summary>
        public PaymentScheme Scheme { get; set; }

        /// <summary>
        /// The network identifier (e.g., "base-sepolia").
        /// </summary>
        public string? Network { get; set; }


        /// <summary>
        /// The MIME type of the resource.
        /// </summary>
        public required string MimeType { get; set; }

        /// <summary>
        /// The maximum amount required in atomic units.
        /// </summary>
        public required BigInteger MaxAmountRequired { get; set; }

        /// <summary>
        /// The asset symbol (e.g., "USDC").
        /// </summary>
        public required string Asset { get; set; }

        /// <summary>
        /// The pay-to wallet address.
        /// </summary>
        public string? PayTo { get; set; }

        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Set to true to enable matching routes based on query string parameters.
        /// </summary>
        public bool EnableQueryStringMatching { get; set; }

    }
}
