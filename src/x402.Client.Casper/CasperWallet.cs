using System.Security.Cryptography;
using Casper.Network.SDK.Types;

namespace x402.Client.Casper
{
    /// <summary>
    /// An x402 wallet that pays on a Casper network, signing CEP-18 transfer
    /// authorizations with a Casper Ed25519 or secp256k1 key.
    /// </summary>
    public partial class CasperWallet : BaseWallet
    {
        /// <summary>
        /// The payer address as it appears in the payment authorization: the
        /// account hash prefixed with the <c>00</c> tag, 66 hex characters.
        /// </summary>
        public string OwnerAddress { get; }

        /// <summary>
        /// The payer public key, hex encoded including its algorithm prefix byte
        /// (<c>01</c> for Ed25519, <c>02</c> for secp256k1).
        /// </summary>
        public string PublicKeyHex { get; }

        /// <summary>
        /// How far in the past the authorization becomes valid. The default of ten
        /// minutes absorbs clock skew between the payer and the facilitator.
        /// </summary>
        public TimeSpan AddValidAfterFromNow { get; set; } = TimeSpan.FromMinutes(-10);

        /// <summary>
        /// How long the authorization stays valid when the payment requirements do
        /// not state a timeout of their own.
        /// </summary>
        public TimeSpan AddValidBeforeFromNow { get; set; } = TimeSpan.FromMinutes(15);

        private readonly KeyPair? keyPair;
        private readonly KeyAlgo keyAlgorithm;
        private readonly Func<byte[], Task<byte[]>>? signFunction;

        /// <summary>
        /// Creates a wallet from a Casper key pair.
        /// </summary>
        /// <param name="keyPair">The Casper key pair used to sign authorizations.</param>
        /// <param name="network">CAIP-2 network identifier, for example <c>casper:casper-test</c>.</param>
        public CasperWallet(KeyPair keyPair, string network) : base(network)
        {
            ArgumentNullException.ThrowIfNull(keyPair);

            this.keyPair = keyPair;
            keyAlgorithm = keyPair.PublicKey.KeyAlgorithm;
            PublicKeyHex = keyPair.PublicKey.ToString()!;
            OwnerAddress = ToAuthorizationAddress(keyPair.PublicKey);
        }

        /// <summary>
        /// Creates a wallet backed by an external signer, for example a hardware
        /// wallet or a remote key management service.
        /// </summary>
        /// <param name="signFunction">
        /// Signs the 32 byte EIP-712 digest and returns the 64 byte raw signature,
        /// without the algorithm prefix byte.
        /// </param>
        /// <param name="publicKeyHex">The payer public key, hex encoded with its algorithm prefix.</param>
        /// <param name="network">CAIP-2 network identifier.</param>
        public CasperWallet(Func<byte[], Task<byte[]>> signFunction, string publicKeyHex, string network) : base(network)
        {
            ArgumentNullException.ThrowIfNull(signFunction);
            ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyHex);

            this.signFunction = signFunction;

            var publicKey = PublicKey.FromHexString(publicKeyHex);
            keyAlgorithm = publicKey.KeyAlgorithm;
            PublicKeyHex = publicKeyHex;
            OwnerAddress = ToAuthorizationAddress(publicKey);
        }

        /// <summary>
        /// Creates a wallet from a PEM encoded private key file, the format the
        /// Casper client and the cspr.live wallet export.
        /// </summary>
        /// <param name="pemFilePath">Path to the PEM encoded private key.</param>
        /// <param name="network">CAIP-2 network identifier.</param>
        /// <returns>The wallet.</returns>
        public static CasperWallet FromPem(string pemFilePath, string network)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pemFilePath);

            return new CasperWallet(KeyPair.FromPem(pemFilePath), network);
        }

        /// <summary>
        /// Signs a digest and prefixes the algorithm tag byte, producing the 65 byte
        /// signature the Casper facilitator expects.
        /// </summary>
        /// <param name="digest">The 32 byte EIP-712 digest.</param>
        /// <returns>The 65 byte algorithm tagged signature.</returns>
        private async Task<byte[]> SignAsync(byte[] digest)
        {
            ArgumentNullException.ThrowIfNull(digest);

            byte[] rawSignature;
            if (signFunction != null)
            {
                rawSignature = await signFunction(digest).ConfigureAwait(false);
            }
            else if (keyPair != null)
            {
                rawSignature = keyPair.Sign(digest);
            }
            else
            {
                throw new InvalidOperationException("No signing key available for this wallet.");
            }

            if (rawSignature.Length != 64)
                throw new InvalidOperationException($"Expected a 64 byte raw Casper signature, got {rawSignature.Length} bytes.");

            return Signature.FromRawBytes(rawSignature, keyAlgorithm).GetBytes();
        }

        /// <summary>
        /// Converts a public key to the 33 byte authorization address used by the
        /// CEP-18 transfer authorization: the account hash behind a <c>00</c> tag.
        /// </summary>
        /// <param name="publicKey">The Casper public key.</param>
        /// <returns>66 lowercase hex characters.</returns>
        private static string ToAuthorizationAddress(PublicKey publicKey)
        {
            // GetAccountHash() returns the "account-hash-" prefixed, checksummed form.
            var accountHash = publicKey.GetAccountHash();
            const string prefix = "account-hash-";
            if (accountHash.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                accountHash = accountHash[prefix.Length..];

            return "00" + accountHash.ToLowerInvariant();
        }

        /// <summary>
        /// Generates the 32 byte replay protection nonce.
        /// </summary>
        /// <returns>32 random bytes.</returns>
        private static byte[] GenerateNonce() => RandomNumberGenerator.GetBytes(32);
    }
}
