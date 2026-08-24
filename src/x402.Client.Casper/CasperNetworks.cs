namespace x402.Client.Casper
{
    /// <summary>
    /// Casper network identifiers and settlement asset constants used by x402.
    /// </summary>
    public static class CasperNetworks
    {
        /// <summary>CAIP-2 identifier of Casper mainnet.</summary>
        public const string Mainnet = "casper:casper";

        /// <summary>CAIP-2 identifier of the Casper testnet.</summary>
        public const string Testnet = "casper:casper-test";

        /// <summary>
        /// Wrapped CSPR (CEP-18) contract package hash on the Casper testnet, the default
        /// settlement asset for x402 payments on that network.
        /// </summary>
        public const string TestnetWCsprPackageHash = "3d80df21ba4ee4d66a2a1f60c32570dd5685e4b279f6538162a5fd1314847c1e";

        /// <summary>Wrapped CSPR uses 9 decimals, unlike the 6 of USDC.</summary>
        public const int WCsprDecimals = 9;

        /// <summary>Token name that Wrapped CSPR reports, used in the EIP-712 domain.</summary>
        public const string WCsprName = "Wrapped CSPR";

        /// <summary>Token version that Wrapped CSPR reports, used in the EIP-712 domain.</summary>
        public const string WCsprVersion = "1";

        /// <summary>
        /// Public Casper facilitator, which verifies and settles payments on both
        /// Casper networks.
        /// </summary>
        public const string FacilitatorUrl = "https://x402-facilitator.cspr.cloud";

        /// <summary>
        /// Returns whether a network identifier names a Casper network.
        /// </summary>
        /// <param name="network">CAIP-2 network identifier.</param>
        /// <returns><see langword="true"/> for a Casper network.</returns>
        public static bool IsCasperNetwork(string? network)
        {
            if (string.IsNullOrWhiteSpace(network))
                return false;

            return network.StartsWith("casper:", StringComparison.OrdinalIgnoreCase);
        }
    }
}
