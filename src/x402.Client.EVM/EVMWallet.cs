using Nethereum.ABI.EIP712;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;
using Nethereum.Web3.Accounts;
using System.Numerics;
using System.Security.Cryptography;
using x402.Core.Models.v1;

namespace x402.Client.EVM
{
    public class EVMWallet : BaseWallet
    {
        public Account Account { get; }
        public ulong ChainId { get; }

        byte[] privateKey;

        public EVMWallet(string hexPrivateKey, ulong chainId)
        {
            if (hexPrivateKey.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hexPrivateKey = hexPrivateKey.Substring(2);

            // Convert hex string to byte[]
            privateKey = hexPrivateKey.HexToByteArray();

            Account = new Nethereum.Web3.Accounts.Account(privateKey);
            ChainId = chainId;
        }

        public EVMWallet(byte[] privateKey, ulong chainId)
        {
            // Convert hex string to byte[]
            this.privateKey = privateKey;

            Account = new Nethereum.Web3.Accounts.Account(privateKey);
            ChainId = chainId;
        }

        public static EVMWallet FromMnemonic(string mnemonic, string password, int accountIndex, ulong chainId)
        {
            var wallet = new Nethereum.HdWallet.Wallet(mnemonic, password);
            var account = wallet.GetAccount(accountIndex);
            return new EVMWallet(account.PrivateKey, chainId);
        }

        public override PaymentPayloadHeader CreateHeader(PaymentRequirements requirement, CancellationToken cancellationToken = default)
        {
            // Prepare EIP-3009 TransferWithAuthorization fields
            string tokenName = requirement.Extra?.Name ?? string.Empty;
            string tokenVersion = requirement.Extra?.Version ?? string.Empty;
            string tokenContractAddress = requirement.Asset;
            string to = requirement.PayTo;

            string from = Account.Address;

            // value should be token units in smallest denomination (uint256)
            var amount = BigInteger.Parse(requirement.MaxAmountRequired);
            var value = new Nethereum.Hex.HexTypes.HexBigInteger(amount);

            // Validity window: use unix timestamps
            ulong validAfter = (ulong)DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(); // valid immediately
            ulong validBefore = (ulong)DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds(); // valid for 15 minutes

            // Create a proper bytes32 nonce: 32 random bytes -> 0x-prefixed hex
            var nonceByte = GenerateBytes32Nonce();

            // Build EIP-712 typed data for EIP-3009
            var typedData = BuildEip3009TypedData(tokenName, tokenVersion, ChainId, tokenContractAddress);

            // Message object with the authorization values
            var message = new TransferWithAuthorization
            {
                From = from,
                To = to,
                Value = value.Value,
                ValidAfter = validAfter,
                ValidBefore = validBefore,
                Nonce = nonceByte
            };

            //  Sign the typed data (EIP-712 v4)
            var ecKey = new EthECKey(privateKey, isPrivate: true);

            var eip712Signer = new Eip712TypedDataSigner();
            string signature = eip712Signer.SignTypedDataV4(message, typedData, ecKey);

            // Recover signer to verify
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
                        Value = value.Value.ToString(), // value as numeric string to avoid precision issues
                        ValidAfter = validAfter.ToString(),
                        ValidBefore = validBefore.ToString(),
                        Nonce = nonceByte.ToHex(prefix: true), //nonce is bytes32: pass as hex string (0x...)
                    }
                },
            };

            return header;
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
