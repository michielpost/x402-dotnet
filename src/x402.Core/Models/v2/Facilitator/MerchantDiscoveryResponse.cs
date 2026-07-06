namespace x402.Core.Models.v2.Facilitator
{
    /// <summary>
    /// Response containing x402 resources associated with a merchant payment address
    /// (GET discovery/merchant). The resources list is empty when no active resources are found.
    /// </summary>
    public class MerchantDiscoveryResponse
    {
        public int X402Version { get; set; } = 2;

        /// <summary>
        /// The merchant's payment address the resources route funds to.
        /// </summary>
        public string PayTo { get; set; } = string.Empty;

        /// <summary>
        /// List of discovered x402 resources associated with the merchant's payTo address.
        /// </summary>
        public List<DiscoveryItem> Resources { get; set; } = new();

        public Pagination Pagination { get; set; } = new();
    }
}
