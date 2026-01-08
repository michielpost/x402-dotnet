using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using x402.Coinbase.Models;
using x402.Core.Models.v2;


namespace x402.Coinbase.IntegrationTests
{
    [TestFixture]
    public class CoinbaseFacilitatorClientTests
    {
        private CoinbaseFacilitatorClient client = null!;

        [OneTimeSetUp]
        public void GlobalSetup()
        {
            // Load secrets from user-secrets or appsettings.json
            var config = new ConfigurationBuilder()
                .AddUserSecrets<CoinbaseFacilitatorClientTests>() // user-secrets
                .AddEnvironmentVariables() // support env vars in CI/CD
                .Build();

            var services = new ServiceCollection();

            services.AddHttpClient(); // registers IHttpClientFactory
            services.Configure<CoinbaseOptions>(config.GetSection("CoinbaseOptions"));

            var provider = services.BuildServiceProvider();

            var coinbaseOptions = provider.GetRequiredService<IOptions<CoinbaseOptions>>();

            client = new CoinbaseFacilitatorClient(new HttpClient(), coinbaseOptions, NullLoggerFactory.Instance);
        }

        [Test]
        public async Task SupportedAsync_ShouldReturnKinds()
        {
            var result = await client.SupportedAsync();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Kinds.Count, Is.GreaterThan(0));
            TestContext.Out.WriteLine($"Supported kinds: {string.Join(", ", result.Kinds.Select(k => k.ToString()))}");
        }

        [Test]
        public async Task VerifyAsync_ShouldReturnVerificationResponse()
        {
            var paymentHeader = "eyJ4NDAyVmVyc2lvbiI6MSwic2NoZW1lIjoiZXhhY3QiLCJuZXR3b3JrIjoiYmFzZS1zZXBvbGlhIiwicGF5bG9hZCI6eyJzaWduYXR1cmUiOiIweGViZGZlZGI4NTE5ZmVjMDg1NDk3NDU3OTI3NmRmNjA2NTc0OTUwYTNhOTg1MGRhNzhlYTFmMDNmNDgwZGY3MjM1ODllZjMwNTRmYzg1YTQ5YjM2ZGJlMmY3YTM5ODA3ZjM4NzJhYWU4NTExNzgzNDMxOWY1NzZmNzc1Yjc2ZTcwMWMiLCJhdXRob3JpemF0aW9uIjp7ImZyb20iOiIweDdEOTU1MTRhRWQ5ZjEzQWE4OUM4ZTVFZDljMjlEMDhFOEU5QmZBMzciLCJ0byI6IjB4MjA5NjkzQmM2YWZjMEM1MzI4YkEzNkZhRjAzQzUxNEVGMzEyMjg3QyIsInZhbHVlIjoiMTAwMDAiLCJ2YWxpZEFmdGVyIjoiMTc1OTQwNDQ2NSIsInZhbGlkQmVmb3JlIjoiMTc1OTQwNTM2NSIsIm5vbmNlIjoiMHhjODE2YjU0ZGViZjJmOTlhMmRlMDRiZTFlNmYxYjJkNjBjNTZlNGU4ZDZjYThiMzI1ZmMzZTcyYmJmM2FiZDY1In19fQ==";
            var payload = PaymentPayloadHeader.FromHeader(paymentHeader);
            var requirements = new PaymentRequirements
            {
                Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
                Amount = "10000",
                PayTo = "0x209693Bc6afc0C5328bA36FaF03C514EF312287C",
                Network = "base-sepolia",
            };

            var result = await client.VerifyAsync(payload, requirements);

            Assert.That(result, Is.Not.Null);
            TestContext.Out.WriteLine($"Verify result: IsValid={result.IsValid}");
        }

        [Test]
        public async Task SettleAsync_ShouldReturnSettlementResponse()
        {
            var paymentHeader = "eyJ4NDAyVmVyc2lvbiI6MSwic2NoZW1lIjoiZXhhY3QiLCJuZXR3b3JrIjoiYmFzZS1zZXBvbGlhIiwicGF5bG9hZCI6eyJzaWduYXR1cmUiOiIweGViZGZlZGI4NTE5ZmVjMDg1NDk3NDU3OTI3NmRmNjA2NTc0OTUwYTNhOTg1MGRhNzhlYTFmMDNmNDgwZGY3MjM1ODllZjMwNTRmYzg1YTQ5YjM2ZGJlMmY3YTM5ODA3ZjM4NzJhYWU4NTExNzgzNDMxOWY1NzZmNzc1Yjc2ZTcwMWMiLCJhdXRob3JpemF0aW9uIjp7ImZyb20iOiIweDdEOTU1MTRhRWQ5ZjEzQWE4OUM4ZTVFZDljMjlEMDhFOEU5QmZBMzciLCJ0byI6IjB4MjA5NjkzQmM2YWZjMEM1MzI4YkEzNkZhRjAzQzUxNEVGMzEyMjg3QyIsInZhbHVlIjoiMTAwMDAiLCJ2YWxpZEFmdGVyIjoiMTc1OTQwNDQ2NSIsInZhbGlkQmVmb3JlIjoiMTc1OTQwNTM2NSIsIm5vbmNlIjoiMHhjODE2YjU0ZGViZjJmOTlhMmRlMDRiZTFlNmYxYjJkNjBjNTZlNGU4ZDZjYThiMzI1ZmMzZTcyYmJmM2FiZDY1In19fQ==";
            var payload = PaymentPayloadHeader.FromHeader(paymentHeader);
            var requirements = new PaymentRequirements
            {
                Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
                Amount = "1",
                PayTo = "0x209693Bc6afc0C5328bA36FaF03C514EF312287C",
                Network = "base-sepolia",
            };

            var result = await client.SettleAsync(payload, requirements);

            Assert.That(result, Is.Not.Null);
            TestContext.Out.WriteLine($"Settle result: Success={result.Success}");
        }

        [Test]
        public async Task DiscoveryAsync_ShouldReturnResources()
        {
            var result = await client.DiscoveryAsync();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Items.Count, Is.GreaterThan(0));
        }
    }
}
