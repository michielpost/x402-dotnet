namespace x402.Client.v2
{
    public class WalletProvider : IWalletProvider
    {
        public IX402WalletV2? Wallet { get; set; }
        public WalletProvider(IX402WalletV2? wallet = null)
        {
            Wallet = wallet;
        }
    }
}
