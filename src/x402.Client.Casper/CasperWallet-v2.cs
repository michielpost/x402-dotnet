using System.Globalization;
using System.Numerics;
using x402.Core.Models.v2;

namespace x402.Client.Casper
{
    public partial class CasperWallet
    {
        /// <summary>
        /// Builds the payment header for a Casper payment requirement by signing a
        /// CEP-18 transfer authorization with the wallet key.
        /// </summary>
        /// <param name="requirement">The payment requirement selected from the 402 challenge.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The payment payload header to send in the PAYMENT header.</returns>
        public override async Task<PaymentPayloadHeader> CreateHeaderAsync(PaymentRequirements requirement, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requirement);

            if (!CasperNetworks.IsCasperNetwork(requirement.Network))
                throw new ArgumentException($"'{requirement.Network}' is not a Casper network.", nameof(requirement));

            // The token name and version are part of the EIP-712 domain, so a payment
            // signed without them would be rejected by the facilitator rather than
            // merely be incomplete.
            var tokenName = requirement.Extra?.Name;
            var tokenVersion = requirement.Extra?.Version;

            if (string.IsNullOrWhiteSpace(tokenName))
                throw new ArgumentException("Casper payment requirements must supply extra.name, the CEP-18 token name.", nameof(requirement));
            if (string.IsNullOrWhiteSpace(tokenVersion))
                throw new ArgumentException("Casper payment requirements must supply extra.version, the CEP-18 token version.", nameof(requirement));

            var contractPackageHash = ParseContractPackageHash(requirement.Asset);
            var to = ParseAuthorizationAddress(requirement.PayTo, nameof(requirement));
            var from = ParseAuthorizationAddress(OwnerAddress, nameof(OwnerAddress));

            if (!BigInteger.TryParse(requirement.Amount, NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
                throw new ArgumentException($"Payment amount '{requirement.Amount}' is not a non-negative integer in motes.", nameof(requirement));

            var now = DateTimeOffset.UtcNow;
            long validAfter = now.Add(AddValidAfterFromNow).ToUnixTimeSeconds();

            // Honour the server's timeout when it states one, so the authorization
            // does not outlive the window the resource is willing to accept.
            var validityWindow = requirement.MaxTimeoutSeconds > 0
                ? TimeSpan.FromSeconds(requirement.MaxTimeoutSeconds)
                : AddValidBeforeFromNow;
            long validBefore = now.Add(validityWindow).ToUnixTimeSeconds();

            var nonce = GenerateNonce();

            var domainSeparator = CasperEip712.HashDomain(tokenName, tokenVersion, requirement.Network, contractPackageHash);
            var structHash = CasperEip712.HashTransferWithAuthorization(from, to, amount, validAfter, validBefore, nonce);
            var digest = CasperEip712.HashTypedData(domainSeparator, structHash);

            var signature = await SignAsync(digest).ConfigureAwait(false);

            return new PaymentPayloadHeader
            {
                X402Version = 2,
                Accepted = requirement,
                Payload = new Payload
                {
                    Signature = ToHex(signature),
                    PublicKey = PublicKeyHex,
                    Authorization = new Authorization
                    {
                        From = OwnerAddress,
                        To = requirement.PayTo,
                        Value = amount.ToString(CultureInfo.InvariantCulture),
                        ValidAfter = validAfter.ToString(CultureInfo.InvariantCulture),
                        ValidBefore = validBefore.ToString(CultureInfo.InvariantCulture),
                        Nonce = ToHex(nonce)
                    }
                }
            };
        }

        /// <summary>
        /// Parses a CEP-18 contract package hash into its 32 raw bytes.
        /// </summary>
        /// <param name="asset">The asset from the payment requirements, 64 hex characters.</param>
        /// <returns>The 32 byte package hash.</returns>
        public static byte[] ParseContractPackageHash(string asset)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(asset);

            var hex = asset.Trim();
            if (hex.StartsWith("hash-", StringComparison.OrdinalIgnoreCase))
                hex = hex[5..];
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex[2..];

            if (hex.Length != 64)
                throw new ArgumentException($"A CEP-18 contract package hash must be 32 bytes, got '{asset}'.", nameof(asset));

            return FromHex(hex, nameof(asset));
        }

        /// <summary>
        /// Parses a 33 byte authorization address, the account hash behind its tag byte.
        /// </summary>
        /// <param name="address">The address to parse.</param>
        /// <param name="paramName">Parameter name used when reporting a malformed address.</param>
        /// <returns>The 33 raw address bytes.</returns>
        public static byte[] ParseAuthorizationAddress(string address, string paramName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(address, paramName);

            var hex = address.Trim();
            if (hex.StartsWith("account-hash-", StringComparison.OrdinalIgnoreCase))
                hex = "00" + hex[13..];
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex[2..];

            if (hex.Length != 66)
            {
                var hint = hex.StartsWith("01", StringComparison.OrdinalIgnoreCase) || hex.StartsWith("02", StringComparison.OrdinalIgnoreCase)
                    ? " Casper x402 authorizations carry the account hash prefixed with '00', not the public key."
                    : string.Empty;
                throw new ArgumentException($"A Casper authorization address must be 33 bytes, got '{address}'.{hint}", paramName);
            }

            return FromHex(hex, paramName);
        }

        /// <summary>
        /// Decodes a hex string into bytes.
        /// </summary>
        /// <param name="hex">The hex characters, without a prefix.</param>
        /// <param name="paramName">Parameter name used when reporting invalid input.</param>
        /// <returns>The decoded bytes.</returns>
        private static byte[] FromHex(string hex, string paramName)
        {
            try
            {
                return Convert.FromHexString(hex);
            }
            catch (FormatException ex)
            {
                throw new ArgumentException($"'{hex}' is not valid hex.", paramName, ex);
            }
        }

        /// <summary>
        /// Encodes bytes as a lowercase hex string, the form the facilitator expects.
        /// </summary>
        /// <param name="bytes">The bytes to encode.</param>
        /// <returns>The lowercase hex string, with no prefix.</returns>
        private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
