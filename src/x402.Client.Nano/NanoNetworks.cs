namespace x402.Client.Nano
{
    /// <summary>
    /// Network and asset identifiers for paying the x402 "exact" scheme on Nano.
    /// </summary>
    public static class NanoNetworks
    {
        /// <summary>CAIP-2 identifier of Nano mainnet.</summary>
        public const string Mainnet = "nano:mainnet";

        /// <summary>The native asset. Nano has no tokens, so there is no contract address.</summary>
        public const string AssetXno = "XNO";
    }
}
