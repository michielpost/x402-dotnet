using x402.Client.Tests.Wallet;

namespace x402.Client.Tests
{
    public class PaymentRequiredHandlerTests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public async Task TestWithAllowance()
        {
            var baseSepoliaAssetId = "0x036CbD53842c5426634e7929541eC2318f3dCF7e";
            var wallet = new TestWallet(new()
            {
                 new() { Asset = baseSepoliaAssetId, TotalAllowance = 1_000_000_000, MaxPerRequestAllowance = 1_000_000_000 }
            });

            var handler = new PaymentRequiredHandler(wallet, maxRetries: 1)
            {
                InnerHandler = new HttpClientHandler()
            };

            var client = new HttpClient(handler);

            var response = await client.GetAsync("https://x402-dotnet.azurewebsites.net/Resource/protected");
            Console.WriteLine($"Final: {(int)response.StatusCode} {response.ReasonPhrase}");
        }
    }
}
