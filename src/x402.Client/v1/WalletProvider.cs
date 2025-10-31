namespace x402.Client.v1
{
    public class WalletProvider : IWalletProvider
    {
        public IX402WalletV1? Wallet { get; set; }

        public WalletProvider(IX402WalletV1? wallet = null)
        {
            Wallet = wallet;
        }
    }
}
