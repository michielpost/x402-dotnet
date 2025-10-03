namespace x402.Models.Responses
{

    /// <summary>
    /// Header for settlement response.
    /// </summary>
    public class SettlementResponseHeader
    {
        /// <summary>
        /// Success indicator.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Transaction hash.
        /// </summary>
        public string TxHash { get; }

        /// <summary>
        /// Network ID.
        /// </summary>
        public string NetworkId { get; }

        /// <summary>
        /// Payer address.
        /// </summary>
        public string? Payer { get; }

        public SettlementResponseHeader(bool success, string txHash, string networkId, string? payer)
        {
            Success = success;
            TxHash = txHash;
            NetworkId = networkId;
            Payer = payer;
        }
    }
}
