using Nethereum.ABI.EIP712;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;
using Nethereum.Web3.Accounts;
using System.Security.Cryptography;

namespace x402.Client.EVM
{
    public partial class EVMWallet : BaseWallet
    {
        public ulong ChainId { get; }
        public string OwnerAddress { get; }

        public TimeSpan AddValidAfterFromNow { get; set; } = TimeSpan.FromMinutes(-1);
        public TimeSpan AddValidBeforeFromNow { get; set; } = TimeSpan.FromMinutes(15);

        public Account? Account { get; }

        byte[]? privateKey;

        private readonly Func<string, Task<string>>? signFunction;

        public EVMWallet(string hexPrivateKey, string network, ulong chainId) : base(network)
        {
            if (hexPrivateKey.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hexPrivateKey = hexPrivateKey.Substring(2);

            // Convert hex string to byte[]
            privateKey = hexPrivateKey.HexToByteArray();

            Account = new Nethereum.Web3.Accounts.Account(privateKey);
            ChainId = chainId;
            OwnerAddress = Account.Address;
        }

        public EVMWallet(byte[] privateKey, string network, ulong chainId) : base(network)
        {
            // Convert hex string to byte[]
            this.privateKey = privateKey;

            Account = new Nethereum.Web3.Accounts.Account(privateKey);
            ChainId = chainId;
            OwnerAddress = Account.Address;
        }

        public EVMWallet(Func<string, Task<string>> signFunction, string ownerAddress, string network, ulong chainId) : base(network)
        {
            this.signFunction = signFunction;
            ChainId = chainId;
            OwnerAddress = ownerAddress;
        }

        public static EVMWallet FromMnemonic(string mnemonic, string password, int accountIndex, string network, ulong chainId)
        {
            var wallet = new Nethereum.HdWallet.Wallet(mnemonic, password);
            var account = wallet.GetAccount(accountIndex);
            return new EVMWallet(account.PrivateKey, network, chainId);
        }

        private Task<string> SignAsync(string data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            if (signFunction != null)
                return signFunction(data);

            //Use internal signing
            //  Sign the typed data (EIP-712 v4)
            var ecKey = new EthECKey(privateKey, isPrivate: true);

            var eip712Signer = new Eip712TypedDataSigner();
            string signature = eip712Signer.SignTypedDataV4(data, ecKey);

            return Task.FromResult(signature);
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
