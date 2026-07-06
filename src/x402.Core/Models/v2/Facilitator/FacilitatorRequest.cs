namespace x402.Core.Models.v2.Facilitator
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

        public required PaymentPayloadHeader PaymentPayload { get; set; }

        /// <summary>
        /// List of accepted payment requirements.
        /// </summary>
        public required PaymentRequirements PaymentRequirements { get; set; }

        /// <summary>
        /// Optional settlement information for schemes that settle less than the authorized
        /// maximum ("upto" and "batch-settlement"). Null for the "exact" scheme.
        /// </summary>
        public SettlementInfo? SettlementInfo { get; set; }

    }

    /// <summary>
    /// Settlement details passed to the facilitator when settling a fraction of the authorized amount.
    /// </summary>
    public class SettlementInfo
    {
        /// <summary>
        /// The actual amount to settle, in atomic units. Must be &lt;= the authorized maximum.
        /// </summary>
        public required string Amount { get; set; }
    }
}
