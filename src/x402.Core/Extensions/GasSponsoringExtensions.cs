using x402.Core.Models.Facilitator;
using x402.Core.Models.v2;

namespace x402.Core.Extensions
{
    /// <summary>
    /// Well-known x402 extension keys.
    /// </summary>
    public static class X402ExtensionKeys
    {
        /// <summary>
        /// The facilitator sponsors the buyer's one-time Permit2 approval using a signed
        /// EIP-2612 permit(). For tokens implementing EIP-2612 (e.g. USDC).
        /// </summary>
        public const string Eip2612GasSponsoring = "eip2612-gas-sponsoring";

        /// <summary>
        /// The facilitator broadcasts a pre-signed approve() transaction on the buyer's behalf.
        /// For generic ERC-20 tokens without EIP-2612 support.
        /// </summary>
        public const string Erc20ApprovalGasSponsoring = "erc20-approval-gas-sponsoring";
    }

    /// <summary>
    /// Helpers to declare gas sponsorship extensions on a route's payment configuration.
    /// Gas sponsorship requires facilitator support: check
    /// <see cref="SupportsExtension(SupportedResponse, string)"/> against the facilitator's
    /// /supported endpoint before declaring one of these on an endpoint.
    /// </summary>
    public static class GasSponsoringExtensions
    {
        /// <summary>
        /// Declares EIP-2612 gas sponsorship, for tokens that implement EIP-2612 permit() (e.g. USDC).
        /// The facilitator uses a signed permit() to approve Permit2 on the buyer's behalf — fully
        /// gasless for the buyer.
        /// </summary>
        public static KeyValuePair<string, ExtensionData> DeclareEip2612GasSponsoringExtension()
        {
            return new KeyValuePair<string, ExtensionData>(X402ExtensionKeys.Eip2612GasSponsoring, new ExtensionData());
        }

        /// <summary>
        /// Declares ERC-20 approval gas sponsorship, for tokens that do not support EIP-2612.
        /// The facilitator broadcasts a pre-signed approve() transaction on the buyer's behalf.
        /// </summary>
        public static KeyValuePair<string, ExtensionData> DeclareErc20ApprovalGasSponsoringExtension()
        {
            return new KeyValuePair<string, ExtensionData>(X402ExtensionKeys.Erc20ApprovalGasSponsoring, new ExtensionData());
        }

        /// <summary>
        /// Adds a declared extension to an extensions dictionary, creating the dictionary when null.
        /// </summary>
        public static Dictionary<string, ExtensionData> With(this Dictionary<string, ExtensionData>? extensions, KeyValuePair<string, ExtensionData> extension)
        {
            extensions ??= new Dictionary<string, ExtensionData>();
            extensions[extension.Key] = extension.Value;
            return extensions;
        }

        /// <summary>
        /// Returns true when the facilitator reports support for the given extension key
        /// (e.g. <see cref="X402ExtensionKeys.Eip2612GasSponsoring"/>) in its /supported response.
        /// </summary>
        public static bool SupportsExtension(this SupportedResponse supportedResponse, string extensionKey)
        {
            if (supportedResponse == null)
            {
                throw new ArgumentNullException(nameof(supportedResponse));
            }

            return supportedResponse.Extensions?.Contains(extensionKey) == true;
        }
    }
}
