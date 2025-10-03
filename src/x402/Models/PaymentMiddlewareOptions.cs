using x402.Facilitator;

namespace x402.Models
{
    public class PaymentMiddlewareOptions
    {
        public required IFacilitatorClient Facilitator { get; set; }

        /// <summary>
        /// Key is the resource path (e.g., "/api/data"), value is the payment requirements for that resource.
        /// </summary>
        public Dictionary<string, PaymentRequirementsConfig> PaymentRequirements { get; set; } = new();

        public string? DefaultPayToAddress { get; set; }
        public string? DefaultNetwork { get; set; }
    }
}
