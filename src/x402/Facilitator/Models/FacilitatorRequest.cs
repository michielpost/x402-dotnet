using x402.Models;

namespace x402.Facilitator.Models
{
    /// <summary>
    /// Request to facilitator
    /// </summary>
    public class FacilitatorRequest
    {
        /// <summary>
        /// The X402 version.
        /// </summary>
        public int X402Version { get; set; }

        public required object PaymentHeader { get; set; }

        /// <summary>
        /// List of accepted payment requirements.
        /// </summary>
        public required PaymentRequirements PaymentRequirements { get; set; }

       
    }
}
