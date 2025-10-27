namespace x402.Core.Models.v1
{
    /// <summary>
    /// Response for 402 Payment Required.
    /// </summary>
    public class PaymentRequiredResponse
    {
        /// <summary>
        /// The X402 version.
        /// </summary>
        public int X402Version { get; set; }

        /// <summary>
        /// List of accepted payment requirements.
        /// </summary>
        public List<PaymentRequirements> Accepts { get; set; } = new List<PaymentRequirements>();

        /// <summary>
        /// Error message, if any.
        /// </summary>
        public string? Error { get; set; }
    }
}
