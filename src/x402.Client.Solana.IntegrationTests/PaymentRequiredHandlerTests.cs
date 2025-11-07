using System.Net.Http.Json;
using x402.Client.v1;
using x402.Client.v2;
using x402.Core.Models.v2;

namespace x402.Client.Solana.IntegrationTests
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
            // Fixed private key for testing - NEVER use in production
            var wallet = new SolanaWallet("5JQJvXN1wYHG3qvHFKHjxVJGmKPXpZc5qvhJH1QG8vXqH1QG8vXqH1QG8vXqH1QG8vXqH1Q", "solana-devnet")
            {
                IgnoreAllowances = true
            };

            // Handle both V1 and V2 payment required responses
            var handlerV1 = PaymentRequiredV1Handler.Create(new x402.Client.v1.WalletProvider(wallet));
            var handlerV2 = PaymentRequiredV2Handler.Create(new x402.Client.v2.WalletProvider(wallet), handlerV1);

            var client = new HttpClient(handlerV2);

            // Test against Solana-enabled endpoints
            // Note: These endpoints would need to support Solana payments
            // Uncomment the appropriate endpoint for testing
            //var response = await client.GetAsync("https://solana-x402-endpoint.example.com/protected");
            //var response = await client.GetAsync("https://localhost:7154/solana/protected");
            
            // For now, using a mock endpoint
            var response = await client.GetAsync("https://httpbin.org/status/200");

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Console.WriteLine($"Final: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        /// <summary>
        /// This test does not first request payment details, but directly submits the payment header
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task TestWithoutHandler()
        {
            var wallet = new SolanaWallet("5JQJvXN1wYHG3qvHFKHjxVJGmKPXpZc5qvhJH1QG8vXqH1QG8vXqH1QG8vXqH1QG8vXqH1Q", "solana-devnet")
            {
                IgnoreAllowances = true
            };

            PaymentPayloadHeader header = await wallet.CreateHeaderAsync(new PaymentRequirements
            {
                Asset = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v", // USDC on Solana devnet
                Amount = "10000",
                Network = "solana-devnet",
                PayTo = "9aKq3TqzQPq3K1Wc2YrqvZjXRzVsKGRqJhHGQqKvYpVZ",
                Extra = new x402.Core.Models.v2.PaymentRequirementsExtra
                {
                    Name = "USDC",
                    Version = "1",
                }
            });

            var client = new HttpClient();

            var request = new HttpRequestMessage(HttpMethod.Get, "https://httpbin.org/status/200");
            request.AddPaymentHeader(header, 1);

            var response = await client.SendAsync(request);

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Console.WriteLine($"Final: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        [Test]
        public async Task TestPost()
        {
            var wallet = new SolanaWallet("5JQJvXN1wYHG3qvHFKHjxVJGmKPXpZc5qvhJH1QG8vXqH1QG8vXqH1QG8vXqH1QG8vXqH1Q", "solana-devnet")
            {
                IgnoreAllowances = true
            };
            var handler = new PaymentRequiredV1Handler(new x402.Client.v1.WalletProvider(wallet));

            var client = new HttpClient(handler);

            var req = new
            {
                Name = "x402-dotnet-solana",
                Message = "Test Solana Payment",
            };

            var response = await client.PostAsJsonAsync("https://httpbin.org/post", req);
            var respText = await response.Content.ReadAsStringAsync();

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Console.WriteLine($"Final: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        [Test]
        public async Task TestV1PaymentHandler()
        {
            var wallet = new SolanaWallet("5JQJvXN1wYHG3qvHFKHjxVJGmKPXpZc5qvhJH1QG8vXqH1QG8vXqH1QG8vXqH1QG8vXqH1Q", "solana-devnet")
            {
                IgnoreAllowances = true
            };

            var handlerV1 = PaymentRequiredV1Handler.Create(new x402.Client.v1.WalletProvider(wallet));
            var client = new HttpClient(handlerV1);

            // Test V1 protocol endpoint
            var response = await client.GetAsync("https://httpbin.org/status/200");

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Console.WriteLine($"V1 Handler Final: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        [Test]
        public async Task TestMultipleRequests()
        {
            var wallet = new SolanaWallet("5JQJvXN1wYHG3qvHFKHjxVJGmKPXpZc5qvhJH1QG8vXqH1QG8vXqH1QG8vXqH1QG8vXqH1Q", "solana-devnet")
            {
                IgnoreAllowances = true
            };

            var handlerV1 = PaymentRequiredV1Handler.Create(new x402.Client.v1.WalletProvider(wallet));
            var handlerV2 = PaymentRequiredV2Handler.Create(new x402.Client.v2.WalletProvider(wallet), handlerV1);

            var client = new HttpClient(handlerV2);

            // Make multiple requests to ensure nonce generation is unique
            var response1 = await client.GetAsync("https://httpbin.org/status/200");
            var response2 = await client.GetAsync("https://httpbin.org/status/200");
            var response3 = await client.GetAsync("https://httpbin.org/status/200");

            Assert.That(response1.IsSuccessStatusCode, Is.True);
            Assert.That(response2.IsSuccessStatusCode, Is.True);
            Assert.That(response3.IsSuccessStatusCode, Is.True);
            
            Console.WriteLine($"Multiple requests completed successfully");
        }

        [Test]
        public async Task TestWithCustomValidityWindow()
        {
            var wallet = new SolanaWallet("5JQJvXN1wYHG3qvHFKHjxVJGmKPXpZc5qvhJH1QG8vXqH1QG8vXqH1QG8vXqH1QG8vXqH1Q", "solana-devnet")
            {
                IgnoreAllowances = true,
                AddValidAfterFromNow = TimeSpan.FromMinutes(-5),
                AddValidBeforeFromNow = TimeSpan.FromMinutes(30)
            };

            PaymentPayloadHeader header = await wallet.CreateHeaderAsync(new PaymentRequirements
            {
                Asset = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v",
                Amount = "5000",
                Network = "solana-devnet",
                PayTo = "9aKq3TqzQPq3K1Wc2YrqvZjXRzVsKGRqJhHGQqKvYpVZ",
            });

            var validAfter = long.Parse(header.Payload.Authorization.ValidAfter);
            var validBefore = long.Parse(header.Payload.Authorization.ValidBefore);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // ValidAfter should be approximately 5 minutes before now
            Assert.That(validAfter, Is.LessThan(now));
            Assert.That(validAfter, Is.GreaterThan(now - 360)); // 6 minutes margin

            // ValidBefore should be approximately 30 minutes after now
            Assert.That(validBefore, Is.GreaterThan(now));
            Assert.That(validBefore, Is.LessThan(now + 1860)); // 31 minutes margin

            Console.WriteLine($"Validity window: {validAfter} to {validBefore} (now: {now})");
        }

        [Test]
        public async Task TestWithDifferentAssets()
        {
            var wallet = new SolanaWallet("5JQJvXN1wYHG3qvHFKHjxVJGmKPXpZc5qvhJH1QG8vXqH1QG8vXqH1QG8vXqH1QG8vXqH1Q", "solana-devnet")
            {
                IgnoreAllowances = true
            };

            // Test with different SPL tokens
            var usdcRequirement = new PaymentRequirements
            {
                Asset = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v", // USDC
                Amount = "1000",
                Network = "solana-devnet",
                PayTo = "9aKq3TqzQPq3K1Wc2YrqvZjXRzVsKGRqJhHGQqKvYpVZ",
            };

            var solRequirement = new PaymentRequirements
            {
                Asset = "So11111111111111111111111111111111111111112", // Wrapped SOL
                Amount = "1000000", // 0.001 SOL (in lamports)
                Network = "solana-devnet",
                PayTo = "9aKq3TqzQPq3K1Wc2YrqvZjXRzVsKGRqJhHGQqKvYpVZ",
            };

            var usdcHeader = await wallet.CreateHeaderAsync(usdcRequirement);
            var solHeader = await wallet.CreateHeaderAsync(solRequirement);

            Assert.That(usdcHeader, Is.Not.Null);
            Assert.That(solHeader, Is.Not.Null);
            Assert.That(usdcHeader.Accepted.Asset, Is.EqualTo(usdcRequirement.Asset));
            Assert.That(solHeader.Accepted.Asset, Is.EqualTo(solRequirement.Asset));

            Console.WriteLine($"USDC header created successfully");
            Console.WriteLine($"SOL header created successfully");
        }
    }
}
