using System.Numerics;
using x402.Enums;

namespace x402.Attributes
{
    /// <summary>
    /// Attribute to specify payment requirements on controller actions or endpoints.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public class PaymentRequiredAttribute : Attribute
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
        public required string PayTo { get; set; }

        /// <summary>
        /// Creates a payment required attribute with the specified price.
        /// </summary>
        /// <param name="price">Payment amount in atomic units as string.</param>
        public PaymentRequiredAttribute(BigInteger price, string asset, string payTo, string? network = null, PaymentScheme scheme = PaymentScheme.Exact)
        {
            MaxAmountRequired = price;
            Asset = asset;
            PayTo = payTo;
            Network = network;
            Scheme = scheme;
        }
    }
}
