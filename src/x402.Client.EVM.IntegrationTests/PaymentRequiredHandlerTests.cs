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
            var wallet = new EVMWallet("0x0123454242abcdef0123456789abcdef0123456789abcdef0123456789abcdef", 84532UL) //base-sepolia chain ID
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
