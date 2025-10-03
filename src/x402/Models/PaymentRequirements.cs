using System.Text.Json.Serialization;
using x402.Enums;

namespace x402.Models
{
    /// <summary>
    /// Represents the requirements for a payment.
    /// </summary>
    public class PaymentRequirements
    {
        /// <summary>
        /// The payment scheme (e.g., "exact").
        /// </summary>
        public PaymentScheme Scheme { get; set; }

        /// <summary>
        /// The network identifier (e.g., "base-sepolia").
        /// </summary>
        public required string Network { get; set; }

        /// <summary>
        /// The maximum amount required in atomic units.
        /// </summary>
        public required string MaxAmountRequired { get; set; }

        /// <summary>
        /// The asset symbol (e.g., "USDC").
        /// </summary>
        public required string Asset { get; set; }

        /// <summary>
        /// The resource path.
        /// </summary>
        public required string Resource { get; set; }

        /// <summary>
        /// The MIME type of the resource.
        /// </summary>
        public required string MimeType { get; set; }

        /// <summary>
        /// The pay-to wallet address.
        /// </summary>
        public required string PayTo { get; set; }

        /// <summary>
        /// The maximum timeout in seconds.
        /// </summary>
        public int MaxTimeoutSeconds { get; set; } = 10;

        public required string Description { get; set; }
        
        public OutputSchema? OutputSchema { get; set; }
        public Extra? Extra { get; set; }

    }

    public class OutputSchema
    {
        public string Data { get; set; } = "string";
    }

    public class Extra
    {
        public string GasLimit { get; set; } = "1000000";
    }
}
