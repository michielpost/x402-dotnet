using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using x402.Core.Enums;
using x402.Core.Models.Facilitator;
using x402.Facilitator;
using x402.Models;

namespace x402.Tests
{
    [TestFixture]
    public class PaymentMiddlewareTests
    {

        private static IHost BuildHost(PaymentMiddlewareOptions options, IFacilitatorClient facilitatorClient)
        {
            return new HostBuilder()
                .ConfigureLogging(b => b.AddDebug().AddConsole().SetMinimumLevel(LogLevel.Debug))
                .ConfigureWebHost(builder =>
                {
                    builder.UseTestServer();
                    builder.ConfigureServices(s => { s.AddSingleton<IFacilitatorClient>(facilitatorClient); });
                    builder.Configure(app =>
                    {
                        app.UsePaymentMiddleware(options);
                        app.Run(async ctx => await ctx.Response.WriteAsync("ok"));
                    });
                }).Start();
        }

        private static string CreateHeaderB64(string resource)
        {
            var headerJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                x402Version = 1,
                scheme = "exact",
                network = "base-sepolia",
                payload = new Dictionary<string, object?>
                {
                    { "authorization", new Dictionary<string, object?> { { "from", "0xabc" } } },
                    { "resource", resource }
                }
            }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(headerJson));
        }

        [Test]
        public async Task NoConfiguredPath_AllowsPipeline()
        {
            var options = new PaymentMiddlewareOptions
            {
                PaymentRequirements = new Dictionary<string, PaymentRequirementsConfig>()
            };

            using var host = BuildHost(options, new FakeFacilitatorClient());
            var client = host.GetTestClient();
            var resp = await client.GetAsync("/free");
            Assert.That(resp.IsSuccessStatusCode, Is.True);
        }

        [Test]
        public async Task ConfiguredPath_NoHeader_Returns402()
        {
            var options = new PaymentMiddlewareOptions
            {
                DefaultNetwork = "base-sepolia",
                DefaultPayToAddress = "0x0000000000000000000000000000000000000001",
                PaymentRequirements = new Dictionary<string, PaymentRequirementsConfig>
                {
                    ["/pay"] = new PaymentRequirementsConfig
                    {
                        Scheme = PaymentScheme.Exact,
                        MaxAmountRequired = "100000",
                        Asset = "USDC",
                        MimeType = "application/json",
                        Description = "unit"
                    }
                }
            };

            using var host = BuildHost(options, new FakeFacilitatorClient());
            var client = host.GetTestClient();
            var resp = await client.GetAsync("/pay");
            Assert.That(resp.StatusCode, Is.EqualTo((System.Net.HttpStatusCode)StatusCodes.Status402PaymentRequired));
        }

        [Test]
        public async Task ConfiguredPath_ValidHeader_AllowsAndSetsResponseHeader()
        {
            var fake = new FakeFacilitatorClient
            {
                VerifyAsyncImpl = (_, _) => Task.FromResult(new VerificationResponse { IsValid = true }),
                SettleAsyncImpl = (_, req) => Task.FromResult(new SettlementResponse { Success = true, Transaction = "0xsettle", Network = req.Network })
            };
            var options = new PaymentMiddlewareOptions
            {
                DefaultNetwork = "base-sepolia",
                DefaultPayToAddress = "0x0000000000000000000000000000000000000001",
                PaymentRequirements = new Dictionary<string, PaymentRequirementsConfig>
                {
                    ["/payok"] = new PaymentRequirementsConfig
                    {
                        Scheme = PaymentScheme.Exact,
                        MaxAmountRequired = "100000",
                        Asset = "USDC",
                        MimeType = "application/json",
                        Description = "unit"
                    }
                }
            };

            using var host = BuildHost(options, fake);
            var client = host.GetTestClient();
            var request = new HttpRequestMessage(HttpMethod.Get, "/payok");
            request.Headers.Add("X-PAYMENT", CreateHeaderB64("http://localhost/payok"));
            var resp = await client.SendAsync(request);
            Assert.That(resp.IsSuccessStatusCode, Is.True);
            Assert.That(resp.Headers.Contains("X-PAYMENT-RESPONSE"), Is.True);
        }
    }
}


