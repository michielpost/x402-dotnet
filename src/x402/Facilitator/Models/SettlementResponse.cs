namespace x402.Facilitator.Models
{
    /// <summary>
    /// Response from settlement operation.
    /// </summary>
    public class SettlementResponse
    {
        /// <summary>
        /// Whether the settlement was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The transaction hash, if successful.
        /// </summary>
        public string? TxHash { get; set; }

        /// <summary>
        /// The network ID, if successful.
        /// </summary>
        public string? NetworkId { get; set; }

        /// <summary>
        /// Error message, if not successful.
        /// </summary>
        public string? Error { get; set; }
    }
}
