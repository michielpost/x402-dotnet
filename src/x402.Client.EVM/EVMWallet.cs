using Nethereum.ABI.EIP712;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;
using System.Numerics;
using System.Security.Cryptography;
using x402.Core.Models;

namespace x402.Client.EVM
{
    public class EVMWallet : BaseWallet
    {
        // ------------------------------
        // 1) Choose chain / network info
        // ------------------------------
        // Base Sepolia chain id (testnet): 84532. Example public RPCs: https://sepolia.base.org, https://base-sepolia-rpc.publicnode.com
        // (Use your own provider / API key in production.) See docs for up-to-date endpoints. 
        ulong chainId = 84532UL; //TODO: Make dynamic
        byte[] privateKey;
        Nethereum.Web3.Accounts.Account account;
        private readonly string hexPrivateKey;

        public EVMWallet(string hexPrivateKey)
        {
            if (hexPrivateKey.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hexPrivateKey = hexPrivateKey.Substring(2);

            // 2) Convert hex string to byte[]
            privateKey = hexPrivateKey.HexToByteArray();

            account = new Nethereum.Web3.Accounts.Account(hexPrivateKey);
            this.hexPrivateKey = hexPrivateKey;
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
            ulong validAfter = (ulong)DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(); // valid immediately
            ulong validBefore = (ulong)DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds(); // valid for 15 minutes

            // Create a proper bytes32 nonce: 32 random bytes -> 0x-prefixed hex
            var nonceByte = GenerateBytes32Nonce();

            // ------------------------------
            // 4) Build EIP-712 typed data for EIP-3009
            // ------------------------------
            var typedData = BuildEip3009TypedData(tokenName, tokenVersion, chainId, tokenContractAddress);

            // Message object with the authorization values. Note:
            // - value as numeric string to avoid precision issues
            // - nonce is bytes32: pass as hex string (0x...)
            var message = new TransferWithAuthorization
            {
                From = from,
                To = to,
                Value = value.Value,
                ValidAfter = validAfter,
                ValidBefore = validBefore,
                Nonce = nonceByte
            };

            // ------------------------------
            // 5) Sign the typed data (EIP-712 v4)
            // ------------------------------
            var ecKey = new EthECKey(hexPrivateKey);

            var eip712Signer = new Eip712TypedDataSigner();
            string signature = eip712Signer.SignTypedDataV4(message, typedData, ecKey);

            //Console.WriteLine($"Signature (65-byte hex): {signature}");

            // ------------------------------
            // 6) Recover signer to verify
            // ------------------------------
            // var recoveredAddress = eip712Signer.RecoverFromSignatureV4(message, typedData, signature);
            //Console.WriteLine($"Recovered signer address: {recoveredAddress}");
            //Console.WriteLine($"Signer matches 'from' ? {string.Equals(recoveredAddress, from, StringComparison.OrdinalIgnoreCase)}\n");

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
                        From = from,
                        To = to,
                        Value = value.Value.ToString(),
                        ValidAfter = validAfter.ToString(),
                        ValidBefore = validBefore.ToString(),
                        Nonce = nonceByte.ToHex(prefix: true),
                    }
                },
            };

            return Task.FromResult(header);
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
            return new TypedData<Domain>
            {
                Domain = new Domain
                {
                    Name = tokenName,
                    Version = tokenVersion,
                    ChainId = chainId,
                    VerifyingContract = verifyingContract
                },
                Types = MemberDescriptionFactory.GetTypesMemberDescription(typeof(Domain), typeof(TransferWithAuthorization)),
                PrimaryType = nameof(TransferWithAuthorization),
            };
        }

    }
}
