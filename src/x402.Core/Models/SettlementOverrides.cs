namespace x402.Core.Models
{
    /// <summary>
    /// Overrides applied at settlement time. Used with the "upto" and "batch-settlement" schemes
    /// to settle only the amount that was actually used, instead of the authorized maximum.
    /// </summary>
    public class SettlementOverrides
    {
        /// <summary>
        /// The amount to settle. Three formats are supported:
        /// <list type="bullet">
        /// <item><description>Raw atomic units, e.g. "1000" settles exactly 1,000 atomic units of the token.</description></item>
        /// <item><description>Percentage of the authorized maximum, e.g. "50%" or "33.33%" (up to two decimal places, floored to the nearest atomic unit).</description></item>
        /// <item><description>Dollar price, e.g. "$0.05", converted to atomic units using the asset's decimals.</description></item>
        /// </list>
        /// The resolved amount must always be less than or equal to the authorized maximum.
        /// If the amount resolves to 0, no on-chain transaction occurs and the client is not charged.
        /// </summary>
        public required string Amount { get; set; }
    }
}
