using Nethereum.ABI.EIP712;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;
using System.Numerics;
using System.Security.Cryptography;
using x402.Models;

namespace x402.Client.EVM
{
    public class EVMWallet : BaseWallet
    {
        // ------------------------------
        // 1) Choose chain / network info
        // ------------------------------
        // Base Sepolia chain id (testnet): 84532. Example public RPCs: https://sepolia.base.org, https://base-sepolia-rpc.publicnode.com
        // (Use your own provider / API key in production.) See docs for up-to-date endpoints. 
        ulong chainId = 84532UL;
        byte[] privateKey;
        Nethereum.Web3.Accounts.Account account;

        public EVMWallet(string hexPrivateKey)
        {
            if (hexPrivateKey.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hexPrivateKey = hexPrivateKey.Substring(2);

            // 2) Convert hex string to byte[]
            privateKey = hexPrivateKey.HexToByteArray();

            account = new Nethereum.Web3.Accounts.Account(hexPrivateKey);

        }

        protected override Task<PaymentPayloadHeader> CreateHeaderAsync(PaymentRequirements requirement, CancellationToken cancellationToken)
        {
            // ------------------------------
            // 3) Prepare EIP-3009 TransferWithAuthorization fields
            // ------------------------------
            // Replace the token address with the ERC20 token contract that implements transferWithAuthorization on Base Sepolia (if any).
            string tokenName = requirement.Extra?.Name ?? string.Empty;
            string tokenVersion = requirement.Extra?.Version ?? string.Empty;
            string tokenContractAddress = requirement.Asset;
            string to = requirement.PayTo;

            string from = account.Address;
            // value should be token units in smallest denomination (uint256). For demo, use a small number.
            var amount = BigInteger.Parse(requirement.MaxAmountRequired);
            var value = new Nethereum.Hex.HexTypes.HexBigInteger(amount);

            // Validity window: use unix timestamps
            ulong validAfter = (ulong)DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds(); // valid immediately
            ulong validBefore = (ulong)DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds(); // valid for 15 minutes

            // Create a proper bytes32 nonce: 32 random bytes -> 0x-prefixed hex
            string nonce = GenerateBytes32NonceHex();
            var nonceByte = GenerateBytes32Nonce();

            // ------------------------------
            // 4) Build EIP-712 typed data for EIP-3009
            // ------------------------------
            var typedData = BuildEip3009TypedData(tokenName, tokenVersion, chainId, tokenContractAddress);

            // Message object with the authorization values. Note:
            // - value as numeric string to avoid precision issues
            // - nonce is bytes32: pass as hex string (0x...)
            var message = new Dictionary<string, object>
            {
                { "from", from },
                { "to", to },
                { "value", (int)value.Value },
                { "validAfter", validAfter },
                { "validBefore", validBefore },
                { "nonce", nonce } // bytes32 hex string
            };
            //var message = new TransferWithAuthorization
            //{
            //    From = from,
            //    To = to,
            //    Value = amount,
            //    ValidAfter = validAfter,
            //    ValidBefore = validBefore,
            //    Nonce = nonceByte
            //};

            // ------------------------------
            // 5) Sign the typed data (EIP-712 v4)
            // ------------------------------
            var eip712Signer = new Eip712TypedDataSigner();
            var ecKey = new EthECKey(privateKey, true);

            string signature = eip712Signer.SignTypedDataV4(message, typedData, ecKey);
            Console.WriteLine($"Signature (65-byte hex): {signature}");

            // ------------------------------
            // 6) Recover signer to verify
            // ------------------------------
            var recoveredAddress = eip712Signer.RecoverFromSignatureV4(message, typedData, signature);
            Console.WriteLine($"Recovered signer address: {recoveredAddress}");
            Console.WriteLine($"Signer matches 'from' ? {string.Equals(recoveredAddress, from, StringComparison.OrdinalIgnoreCase)}\n");

            var header = new PaymentPayloadHeader()
            {
                Network = requirement.Network,
                Scheme = requirement.Scheme,
                X402Version = 1,
                Payload = new Payload
                {
                    Resource = requirement.Resource,
                    Signature = signature,
                    Authorization = new Authorization
                    {
                        Value = value.Value.ToString(),
                        From = from,
                        To = to,
                        Nonce = nonce,
                        ValidAfter = validAfter.ToString(),
                        ValidBefore = validBefore.ToString(),
                    }
                },
            };

            return Task.FromResult(header);
        }

        private static string GenerateBytes32NonceHex()
        {
            // generate 32 random bytes and return 0x-prefixed hex
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return "0x" + bytes.ToHex();
        }

        private static byte[] GenerateBytes32Nonce()
        {
            // generate 32 random bytes and return 0x-prefixed hex
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return bytes;
        }

        private static TypedData<Domain> BuildEip3009TypedData(string tokenName, string tokenVersion, ulong chainId, string verifyingContract)
        {
            var domain = new Domain
            {
                Name = tokenName,
                Version = tokenVersion,
                ChainId = chainId,
                VerifyingContract = verifyingContract
            };

            return new TypedData<Domain>
            {
                Types = MemberDescriptionFactory.GetTypesMemberDescription(typeof(Domain), typeof(TransferWithAuthorization)),
                Domain = domain,
                PrimaryType = nameof(TransferWithAuthorization),
            };
        }


    }


    [Struct("TransferWithAuthorization")]
    public class TransferWithAuthorization
    {
        [Parameter("address", "from", order: 1)]
        public virtual required string From { get; set; }

        [Parameter("address", "to", order: 2)]
        public virtual required string To { get; set; }

        [Parameter("uint256", "value", order: 3)]
        public virtual BigInteger Value { get; set; }

        [Parameter("uint256", "validAfter", order: 4)]
        public virtual BigInteger ValidAfter { get; set; }

        [Parameter("uint256", "validBefore", order: 5)]
        public virtual BigInteger ValidBefore { get; set; }

        [Parameter("bytes32", "nonce", order: 6)]
        public virtual byte[]? Nonce { get; set; }
    }
}
