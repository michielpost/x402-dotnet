using x402.Core.Enums;

namespace x402.Core.Models
{
    public class PaymentRequiredInfo
    {
        public required ResourceInfoBasic? Resource { get; set; }
        public required List<PaymentRequirementsBasic> Accepts { get; set; }

        public bool Discoverable { get; set; }

    }

    public class ResourceInfoBasic
    {
        /// <summary>
        /// Human-readable description of the resource
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// The MIME type of the resource.
        /// </summary>
        public string MimeType { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents the requirements for a payment.
    /// </summary>
    public class PaymentRequirementsBasic
    {
        /// <summary>
        /// The payment scheme (e.g., "exact").
        /// </summary>
        public PaymentScheme Scheme { get; set; }

        /// <summary>
        /// The maximum amount required in atomic units.
        /// </summary>
        public required string Amount { get; set; }

        /// <summary>
        /// The asset contract address
        /// </summary>
        public required string Asset { get; set; }

        /// <summary>
        /// The pay-to wallet address.
        /// </summary>
        public required string PayTo { get; set; }

        /// <summary>
        /// The maximum timeout in seconds.
        /// </summary>
        public int MaxTimeoutSeconds { get; set; } = 60;
    }

}
