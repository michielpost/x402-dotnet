namespace x402.Client.EVM.IntegrationTests
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
            //var baseSepoliaAssetId = "0x036CbD53842c5426634e7929541eC2318f3dCF7e";
            //var wallet = new TestWallet(new()
            //{
            //     new() { Asset = baseSepoliaAssetId, TotalAllowance = 1_000_000_000, MaxPerRequestAllowance = 1_000_000_000 }
            //});


            // Fixed private key (32 bytes hex) for deterministic address; signature will still vary due to nonce/time
            var wallet = new EVMWallet("0x0123454242abcdef0123456789abcdef0123456789abcdef0123456789abcdef")
            {
                IgnoreAllowances = true
            };
            var handler = new PaymentRequiredHandler(wallet);

            var client = new HttpClient(handler);

            //var response = await client.GetAsync("https://x402-dotnet.azurewebsites.net/Resource/protected");
            //var response = await client.GetAsync("https://localhost:7154/resource/protected");
            var response = await client.GetAsync("https://www.x402.org/protected");
            //var response = await client.GetAsync("https://proxy402.com/Z6lePs160M");

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Console.WriteLine($"Final: {(int)response.StatusCode} {response.ReasonPhrase}");
        }
    }
}
