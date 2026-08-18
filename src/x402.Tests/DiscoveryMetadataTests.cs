using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text;
using System.Text.Json;
using x402.Core;
using x402.Core.Enums;
using x402.Core.Interfaces;
using x402.Core.Models;
using x402.Core.Models.v2;
using x402.Facilitator;

namespace x402.Tests
{
    /// <summary>
    /// Verifies that discovery-layer metadata (serviceName, tags, iconUrl) set on a resource
    /// is exposed on the resource object of the 402 PAYMENT-REQUIRED response.
    /// </summary>
    [TestFixture]
    public class DiscoveryMetadataTests
    {
        private static IHost BuildHost(PaymentRequiredInfo paymentRequiredInfo)
        {
            return new HostBuilder()
                .ConfigureWebHost(builder =>
                {
                    builder.UseTestServer();
                    builder.ConfigureServices(services =>
                    {
                        services.AddSingleton<IFacilitatorV2Client>(new FakeFacilitatorClient());
                        services.AddSingleton<X402HandlerV2>();
                        services.AddSingleton<IAssetInfoProvider, AssetInfoProvider>();
                        services.AddHttpContextAccessor();
                    });
                    builder.Configure(app =>
                    {
                        app.Run(async context =>
                        {
                            var handler = context.RequestServices.GetRequiredService<X402HandlerV2>();
                            var result = await handler.HandleX402Async(paymentRequiredInfo).ConfigureAwait(false);
                            if (result.CanContinueRequest)
                            {
                                await context.Response.WriteAsync("ok").ConfigureAwait(false);
                            }
                        });
                    });
                })
                .Start();
        }

        private static PaymentRequiredInfo CreatePaymentRequiredInfo(ResourceInfoBasic resource)
        {
            return new PaymentRequiredInfo
            {
                Resource = resource,
                Discoverable = true,
                Accepts = new List<PaymentRequirementsBasic>
                {
                    new()
                    {
                        Scheme = PaymentScheme.Exact,
                        Amount = "1000",
                        Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
                        PayTo = "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37",
                        Network = "eip155:84532"
                    }
                }
            };
        }

        private static async Task<JsonElement> Get402ResourceAsync(IHost host, string path)
        {
            var client = host.GetTestClient();
            var resp = await client.GetAsync(path);

            Assert.That(resp.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.PaymentRequired));
            Assert.That(resp.Headers.Contains("PAYMENT-REQUIRED"), Is.True);

            var headerJson = Encoding.UTF8.GetString(Convert.FromBase64String(resp.Headers.GetValues("PAYMENT-REQUIRED").Single()));
            using var doc = JsonDocument.Parse(headerJson);
            return doc.RootElement.GetProperty("resource").Clone();
        }

        [Test]
        public async Task Response402_IncludesServiceNameTagsAndIconUrl()
        {
            var info = CreatePaymentRequiredInfo(new ResourceInfoBasic
            {
                Description = "Premium API access.",
                MimeType = "application/json",
                ServiceName = "Premium Data API",
                Tags = new List<string> { "data", "analytics" },
                IconUrl = "https://cdn.example.com/icon.png"
            });

            using var host = BuildHost(info);
            var resource = await Get402ResourceAsync(host, "/metadata");

            Assert.That(resource.GetProperty("description").GetString(), Is.EqualTo("Premium API access."));
            Assert.That(resource.GetProperty("serviceName").GetString(), Is.EqualTo("Premium Data API"));
            Assert.That(resource.GetProperty("iconUrl").GetString(), Is.EqualTo("https://cdn.example.com/icon.png"));
            Assert.That(resource.GetProperty("tags").EnumerateArray().Select(t => t.GetString()),
                Is.EqualTo(new[] { "data", "analytics" }));
        }

        [Test]
        public async Task Response402_OmitsMetadataFieldsWhenNotSet()
        {
            var info = CreatePaymentRequiredInfo(new ResourceInfoBasic
            {
                Description = "Premium API access."
            });

            using var host = BuildHost(info);
            var resource = await Get402ResourceAsync(host, "/no-metadata");

            Assert.That(resource.TryGetProperty("serviceName", out _), Is.False);
            Assert.That(resource.TryGetProperty("tags", out _), Is.False);
            Assert.That(resource.TryGetProperty("iconUrl", out _), Is.False);
        }

        [Test]
        public async Task Response402_UsesExplicitResourceUrlWhenSet()
        {
            var info = CreatePaymentRequiredInfo(new ResourceInfoBasic
            {
                Resource = "https://api.example.com/premium/data"
            });

            using var host = BuildHost(info);
            var resource = await Get402ResourceAsync(host, "/explicit-url");

            Assert.That(resource.GetProperty("url").GetString(), Is.EqualTo("https://api.example.com/premium/data"));
        }
    }
}
