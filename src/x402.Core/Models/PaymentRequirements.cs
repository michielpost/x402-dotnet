using x402.Core.Enums;

namespace x402.Core.Models
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
        /// The pay-to wallet address.
        /// </summary>
        public required string PayTo { get; set; }

        /// <summary>
        /// The resource path.
        /// </summary>
        public string Resource { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable description of the resource
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// The MIME type of the resource.
        /// </summary>
        public string MimeType { get; set; } = string.Empty;

        /// <summary>
        /// JSON schema describing the response format
        /// </summary>
        public OutputSchema OutputSchema { get; set; } = new();

        /// <summary>
        /// The maximum timeout in seconds.
        /// </summary>
        public int MaxTimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// Scheme-specific additional information
        /// </summary>
        public PaymentRequirementsExtra? Extra { get; set; }

    }

    public class PaymentRequirementsExtra
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
    }

    public class OutputSchema
    {
        public Input? Input { get; set; } = new();
        public object? Output { get; set; }
    }

    public class Input
    {
        public bool Discoverable { get; set; } = true;
        public string Method { get; set; } = "GET";
        public string Type { get; set; } = "http";

        //public BodyFields BodyFields { get; set; }
       
        //public string BodyType { get; set; }
        //public string Description { get; set; }
        //public bool Required { get; set; }
    }

    

}
