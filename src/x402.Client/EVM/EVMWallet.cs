using NBitcoin;
using Nethereum.ABI.EIP712;
using Nethereum.HdWallet;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;
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

        static string GenerateBytes32NonceHex()
        {
            // generate 32 random bytes and return 0x-prefixed hex
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return "0x" + bytes.ToHex();
        }

        static TypedData<Domain> BuildEip3009TypedData(string tokenName, string tokenVersion, ulong chainId, string verifyingContract)
        {
            var domain = new Domain
            {
                Name = tokenName,
                Version = tokenVersion,
                ChainId = chainId,
                VerifyingContract = verifyingContract
            };

            var types = new Dictionary<string, MemberDescription[]>
    {
        {
            "EIP712Domain", new []
            {
                new MemberDescription { Name = "name", Type = "string" },
                new MemberDescription { Name = "version", Type = "string" },
                new MemberDescription { Name = "chainId", Type = "uint256" },
                new MemberDescription { Name = "verifyingContract", Type = "address" }
            }
        },
        {
            "TransferWithAuthorization", new []
            {
                new MemberDescription { Name = "from", Type = "address" },
                new MemberDescription { Name = "to", Type = "address" },
                new MemberDescription { Name = "value", Type = "uint256" },
                new MemberDescription { Name = "validAfter", Type = "uint256" },
                new MemberDescription { Name = "validBefore", Type = "uint256" },
                new MemberDescription { Name = "nonce", Type = "bytes32" }
            }
        }
    };

            return new TypedData<Domain>
            {
                Domain = domain,
                Types = types,
                PrimaryType = "TransferWithAuthorization"
            };
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
            var value = new Nethereum.Hex.HexTypes.HexBigInteger(1000);

            // Validity window: use unix timestamps
            ulong validAfter = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(); // valid immediately
            ulong validBefore = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600; // valid for 1 hour

            // Create a proper bytes32 nonce: 32 random bytes -> 0x-prefixed hex
            string nonce = GenerateBytes32NonceHex();

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
                { "value", value.Value.ToString() },
                { "validAfter", validAfter },
                { "validBefore", validBefore },
                { "nonce", nonce } // bytes32 hex string
            };

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
    }
}
