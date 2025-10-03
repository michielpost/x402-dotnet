namespace x402.Models
{
    /// <summary>
    /// Payload specific to the exact scheme.
    /// </summary>
    public class ExactSchemePayload
    {
        /// <summary>
        /// The authorization details.
        /// </summary>
        public Authorization? Authorization { get; set; }
    }

    /// <summary>
    /// Authorization details within ExactSchemePayload.
    /// </summary>
    public class Authorization
    {
        /// <summary>
        /// The payer's wallet address.
        /// </summary>
        public string? From { get; set; }
    }
}
