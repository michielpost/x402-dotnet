using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text;
using System.Text.Json;
using x402.Channels;
using x402.Core;
using x402.Core.Enums;
using x402.Core.Extensions;
using x402.Core.Interfaces;
using x402.Core.Models;
using x402.Core.Models.Facilitator;
using x402.Core.Models.v2;
using x402.Facilitator;

namespace x402.Tests
{
    [TestFixture]
    public class X402HandlerV2UptoTests
    {
        private const string UsdcBaseSepolia = "0x036CbD53842c5426634e7929541eC2318f3dCF7e";
        private const string PayToAddress = "0x0000000000000000000000000000000000000001";

        private static IHost BuildHost(IFacilitatorV2Client facilitator,
            List<PaymentRequirements> requirements,
            SettlementMode mode = SettlementMode.Optimistic,
            Action<HttpContext>? onRequest = null,
            Dictionary<string, ExtensionData>? extensions = null,
            bool useChannelManager = false)
        {
            return new HostBuilder()
                .ConfigureLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Debug))
                .ConfigureWebHost(builder =>
                {
                    builder.UseTestServer();
                    builder.ConfigureServices(services =>
                    {
                        services.AddSingleton(facilitator);
                        services.AddSingleton<X402HandlerV2>();
                        services.AddSingleton<IAssetInfoProvider, AssetInfoProvider>();
                        services.AddHttpContextAccessor();
                        if (useChannelManager)
                        {
                            services.AddX402ChannelManager();
                        }
                    });
                    builder.Configure(app =>
                    {
                        app.Run(async context =>
                        {
                            var x402handler = context.RequestServices.GetRequiredService<X402HandlerV2>();
                            var result = await x402handler.HandleX402Async(
                                new ResourceInfo { Url = $"http://localhost{context.Request.Path}" },
                                requirements,
                                true,
                                mode,
                                extensions: extensions).ConfigureAwait(false);

                            if (result.CanContinueRequest)
                            {
                                onRequest?.Invoke(context);
                                await context.Response.WriteAsync("ok").ConfigureAwait(false);
                            }
                        });
                    });
                })
                .Start();
        }

        private static PaymentRequirements CreateRequirements(PaymentScheme scheme, string maxAmount, string asset = "USDC")
        {
            return new PaymentRequirements
            {
                Scheme = scheme,
                Network = "eip155:84532",
                Amount = maxAmount,
                Asset = asset,
                PayTo = PayToAddress,
            };
        }

        private static string CreateHeaderB64(PaymentRequirements accepted, string authorizedValue, string from = "0xabc")
        {
            var payload = new
            {
                x402Version = 2,
                accepted = accepted,
                payload = new Dictionary<string, object?>
                {
                    { "authorization", new Dictionary<string, object?> {
                        { "from", from },
                        { "to", accepted.PayTo },
                        { "value", authorizedValue },
                        { "validBefore", DateTimeOffset.UtcNow.AddSeconds(5).ToUnixTimeSeconds().ToString() },
                        { "validAfter", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() }
                    } }
                }
            };
            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        private static HttpRequestMessage CreateRequest(string path, PaymentRequirements accepted, string authorizedValue)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("PAYMENT-SIGNATURE", CreateHeaderB64(accepted, authorizedValue));
            return request;
        }

        [Test]
        public async Task Upto_AuthorizationBelowMax_Accepted_AndSettlesAuthorizedAmount()
        {
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements(PaymentScheme.Upto, "100");
            using var host = BuildHost(facilitator, new List<PaymentRequirements> { reqs });
            var client = host.GetTestClient();

            var resp = await client.SendAsync(CreateRequest("/upto", reqs, authorizedValue: "60"));

            Assert.That(resp.IsSuccessStatusCode, Is.True);
            Assert.That(resp.Headers.Contains("PAYMENT-RESPONSE"), Is.True);
            // Without overrides the full authorized amount is settled
            Assert.That(facilitator.LastSettlementAmount, Is.EqualTo("60"));
        }

        [Test]
        public async Task Upto_AuthorizationExceedsMax_Returns402()
        {
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements(PaymentScheme.Upto, "100");
            using var host = BuildHost(facilitator, new List<PaymentRequirements> { reqs });
            var client = host.GetTestClient();

            var resp = await client.SendAsync(CreateRequest("/upto", reqs, authorizedValue: "101"));

            Assert.That(resp.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.PaymentRequired));
            Assert.That(facilitator.SettleCallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task Upto_AuthorizationZero_Returns402()
        {
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements(PaymentScheme.Upto, "100");
            using var host = BuildHost(facilitator, new List<PaymentRequirements> { reqs });
            var client = host.GetTestClient();

            var resp = await client.SendAsync(CreateRequest("/upto", reqs, authorizedValue: "0"));

            Assert.That(resp.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.PaymentRequired));
        }

        [Test]
        public async Task Upto_OverrideRawAmount_SettlesOverriddenAmount()
        {
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements(PaymentScheme.Upto, "100");
            using var host = BuildHost(facilitator, new List<PaymentRequirements> { reqs },
                onRequest: ctx => ctx.SetSettlementOverrides("40"));
            var client = host.GetTestClient();

            var resp = await client.SendAsync(CreateRequest("/upto", reqs, authorizedValue: "100"));

            Assert.That(resp.IsSuccessStatusCode, Is.True);
            Assert.That(facilitator.LastSettlementAmount, Is.EqualTo("40"));
        }

        [Test]
        public async Task Upto_OverridePercentage_SettlesFlooredPercentageOfAuthorized()
        {
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements(PaymentScheme.Upto, "100");
            using var host = BuildHost(facilitator, new List<PaymentRequirements> { reqs },
                onRequest: ctx => ctx.SetSettlementOverrides("33.33%"));
            var client = host.GetTestClient();

            var resp = await client.SendAsync(CreateRequest("/upto", reqs, authorizedValue: "100"));

            Assert.That(resp.IsSuccessStatusCode, Is.True);
            // 33.33% of 100 = 33.33, floored to 33
            Assert.That(facilitator.LastSettlementAmount, Is.EqualTo("33"));
        }

        [Test]
        public async Task Upto_OverrideDollarPrice_SettlesConvertedAtomicUnits()
        {
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements(PaymentScheme.Upto, "100000", asset: UsdcBaseSepolia);
            using var host = BuildHost(facilitator, new List<PaymentRequirements> { reqs },
                onRequest: ctx => ctx.SetSettlementOverrides("$0.05"));
            var client = host.GetTestClient();

            var resp = await client.SendAsync(CreateRequest("/upto", reqs, authorizedValue: "100000"));

            Assert.That(resp.IsSuccessStatusCode, Is.True);
            // $0.05 in 6-decimal USDC = 50,000 atomic units
            Assert.That(facilitator.LastSettlementAmount, Is.EqualTo("50000"));
        }

        [Test]
        public async Task Upto_OverrideZero_SkipsSettlement_AndClientIsNotCharged()
        {
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements(PaymentScheme.Upto, "100");
            using var host = BuildHost(facilitator, new List<PaymentRequirements> { reqs },
                onRequest: ctx => ctx.SetSettlementOverrides("0"));
            var client = host.GetTestClient();

            var resp = await client.SendAsync(CreateRequest("/upto", reqs, authorizedValue: "100"));

            Assert.That(resp.IsSuccessStatusCode, Is.True);
            Assert.That(facilitator.SettleCallCount, Is.EqualTo(0));
            Assert.That(resp.Headers.Contains("PAYMENT-RESPONSE"), Is.True);
        }

        [Test]
        public async Task Upto_OverrideExceedsAuthorized_Pessimistic_Returns402_WithoutSettling()
        {
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements(PaymentScheme.Upto, "100");
            using var host = BuildHost(facilitator, new List<PaymentRequirements> { reqs },
                SettlementMode.Pessimistic,
                onRequest: ctx => ctx.SetSettlementOverrides("200"));
            var client = host.GetTestClient();

            var resp = await client.SendAsync(CreateRequest("/upto", reqs, authorizedValue: "100"));

            Assert.That(resp.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.PaymentRequired));
            Assert.That(facilitator.SettleCallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task Upto_Pessimistic_DefersSettlementUntilOverridesAreKnown()
        {
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements(PaymentScheme.Upto, "100");
            using var host = BuildHost(facilitator, new List<PaymentRequirements> { reqs },
                SettlementMode.Pessimistic,
                onRequest: ctx => ctx.SetSettlementOverrides("40"));
            var client = host.GetTestClient();

            var resp = await client.SendAsync(CreateRequest("/upto", reqs, authorizedValue: "100"));

            Assert.That(resp.IsSuccessStatusCode, Is.True);
            // The override set by the endpoint was honored, proving settlement ran after the handler
            Assert.That(facilitator.LastSettlementAmount, Is.EqualTo("40"));
        }

        [Test]
        public async Task Exact_SettlesWithoutSettlementAmount()
        {
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements(PaymentScheme.Exact, "100");
            using var host = BuildHost(facilitator, new List<PaymentRequirements> { reqs });
            var client = host.GetTestClient();

            var resp = await client.SendAsync(CreateRequest("/exact", reqs, authorizedValue: "100"));

            Assert.That(resp.IsSuccessStatusCode, Is.True);
            Assert.That(facilitator.SettleCallCount, Is.EqualTo(1));
            Assert.That(facilitator.LastSettlementAmount, Is.Null);
        }

        [Test]
        public async Task Exact_AuthorizationBelowAmount_Returns402()
        {
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements(PaymentScheme.Exact, "100");
            using var host = BuildHost(facilitator, new List<PaymentRequirements> { reqs });
            var client = host.GetTestClient();

            var resp = await client.SendAsync(CreateRequest("/exact", reqs, authorizedValue: "60"));

            Assert.That(resp.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.PaymentRequired));
        }

        [Test]
        public async Task BatchSettlement_WithChannelManager_RecordsVoucher_WithoutImmediateSettlement()
        {
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements(PaymentScheme.BatchSettlement, "100");
            using var host = BuildHost(facilitator, new List<PaymentRequirements> { reqs },
                onRequest: ctx => ctx.SetSettlementOverrides("50%"),
                useChannelManager: true);
            var client = host.GetTestClient();

            var resp = await client.SendAsync(CreateRequest("/batch", reqs, authorizedValue: "100"));

            Assert.That(resp.IsSuccessStatusCode, Is.True);
            Assert.That(facilitator.SettleCallCount, Is.EqualTo(0), "batch-settlement must not settle per request");
            Assert.That(resp.Headers.Contains("PAYMENT-RESPONSE"), Is.True);

            var channelManager = host.Services.GetRequiredService<ChannelManager>();
            Assert.That(channelManager.Channels, Has.Count.EqualTo(1));
            var channel = channelManager.Channels[0];
            Assert.That(channel.PendingAmount.ToString(), Is.EqualTo("50"));
            Assert.That(channel.TotalCharged.ToString(), Is.EqualTo("50"));
            Assert.That(channel.AuthorizedAmount.ToString(), Is.EqualTo("100"));
            Assert.That(channel.PendingVoucherCount, Is.EqualTo(1));
        }

        [Test]
        public async Task BatchSettlement_MultipleRequests_AccumulateVouchersOnSameChannel()
        {
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements(PaymentScheme.BatchSettlement, "100");
            using var host = BuildHost(facilitator, new List<PaymentRequirements> { reqs },
                onRequest: ctx => ctx.SetSettlementOverrides("10"),
                useChannelManager: true);
            var client = host.GetTestClient();

            for (int i = 0; i < 3; i++)
            {
                var resp = await client.SendAsync(CreateRequest("/batch", reqs, authorizedValue: "100"));
                Assert.That(resp.IsSuccessStatusCode, Is.True);
            }

            var channelManager = host.Services.GetRequiredService<ChannelManager>();
            Assert.That(channelManager.Channels, Has.Count.EqualTo(1));
            Assert.That(channelManager.Channels[0].PendingVoucherCount, Is.EqualTo(3));
            Assert.That(channelManager.Channels[0].PendingAmount.ToString(), Is.EqualTo("30"));
            Assert.That(facilitator.SettleCallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task BatchSettlement_WithoutChannelManager_FallsBackToPerRequestSettlement()
        {
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements(PaymentScheme.BatchSettlement, "100");
            using var host = BuildHost(facilitator, new List<PaymentRequirements> { reqs },
                onRequest: ctx => ctx.SetSettlementOverrides("25"));
            var client = host.GetTestClient();

            var resp = await client.SendAsync(CreateRequest("/batch", reqs, authorizedValue: "100"));

            Assert.That(resp.IsSuccessStatusCode, Is.True);
            Assert.That(facilitator.SettleCallCount, Is.EqualTo(1));
            Assert.That(facilitator.LastSettlementAmount, Is.EqualTo("25"));
        }

        [Test]
        public async Task Extensions_AreIncludedInPaymentRequiredResponse()
        {
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements(PaymentScheme.Exact, "100");
            var extensions = new Dictionary<string, ExtensionData>()
                .With(GasSponsoringExtensions.DeclareEip2612GasSponsoringExtension());
            using var host = BuildHost(facilitator, new List<PaymentRequirements> { reqs }, extensions: extensions);
            var client = host.GetTestClient();

            // No payment header -> 402 with PAYMENT-REQUIRED header
            var resp = await client.GetAsync("/protected");

            Assert.That(resp.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.PaymentRequired));
            var header = resp.Headers.GetValues("PAYMENT-REQUIRED").First();
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(header));
            Assert.That(json, Does.Contain("eip2612-gas-sponsoring"));
        }

        [Test]
        public async Task HandleX402Async_CopiesAssetTransferMethodIntoRequirements()
        {
            var facilitatorMock = new Mock<IFacilitatorV2Client>();
            var assetInfoProviderMock = new Mock<IAssetInfoProvider>();
            var httpContextAccessorMock = new Mock<IHttpContextAccessor>();

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("localhost");
            httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);
            assetInfoProviderMock.Setup(x => x.GetAssetInfo(It.IsAny<string>()))
                .Returns(new AssetInfo { Network = "eip155:84532", ContractAddress = "test-asset", Name = "Test", Version = "1" });

            var handler = new X402HandlerV2(
                new NullLogger<X402HandlerV2>(),
                facilitatorMock.Object,
                assetInfoProviderMock.Object,
                httpContextAccessorMock.Object);

            var paymentRequiredInfo = new PaymentRequiredInfo
            {
                Accepts =
                [
                    new PaymentRequirementsBasic
                    {
                        Scheme = PaymentScheme.Exact,
                        Asset = "test-asset",
                        Amount = "100",
                        PayTo = "test-payto",
                        AssetTransferMethod = AssetTransferMethods.Permit2
                    }
                ],
                Resource = new ResourceInfoBasic { Description = "test", MimeType = "application/json" }
            };

            var result = await handler.HandleX402Async(paymentRequiredInfo);

            Assert.That(result.PaymentRequirements, Has.Count.EqualTo(1));
            Assert.That(result.PaymentRequirements[0].Extra?.AssetTransferMethod, Is.EqualTo(AssetTransferMethods.Permit2));
        }
    }
}
