using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using x402.Core;
using x402.Core.Enums;
using x402.Core.Interfaces;
using x402.Core.Models;
using x402.Core.Models.Facilitator;
using x402.Facilitator;

namespace x402.Tests
{
    [TestFixture]
    public class X402HandlerTests
    {

        private static IHost BuildHost(IFacilitatorClient facilitator,
            string path,
            PaymentRequirements requirements,
            SettlementMode mode = SettlementMode.Optimistic,
            Func<HttpContext, SettlementResponse?, Exception?, Task>? onSettlement = null,
            Action? onStartingMarker = null)
        {
            return new HostBuilder()
                .ConfigureLogging(b => b.AddDebug().AddConsole().SetMinimumLevel(LogLevel.Debug))
                .ConfigureWebHost(builder =>
                {
                    builder.UseTestServer();
                    builder.ConfigureServices(services =>
                    {
                        services.AddSingleton(facilitator);
                        services.AddSingleton<X402Handler>();
                        services.AddSingleton<IAssetInfoProvider, AssetInfoProvider>();
                        services.AddHttpContextAccessor();

                    });
                    builder.Configure(app =>
                    {
                        app.Run(async context =>
                        {
                            // Test hook to verify Response.OnStarting is reachable in pipeline
                            context.Response.OnStarting(() =>
                            {
                                onStartingMarker?.Invoke();
                                return Task.CompletedTask;
                            });

                            // Invoke handler
                            var x402handler = context.RequestServices.GetRequiredService<X402Handler>();
                            var result = await x402handler.HandleX402Async(
                                requirements,
                                true,
                                mode,
                                onSettlement).ConfigureAwait(false);

                            if (result.CanContinueRequest)
                            {
                                await context.Response.WriteAsync("ok").ConfigureAwait(false);
                            }
                        });
                    });
                })
                .Start();
        }

        private static PaymentRequirements CreateRequirements(string path)
        {
            return new PaymentRequirements
            {
                Scheme = PaymentScheme.Exact,
                Network = "base-sepolia",
                MaxAmountRequired = "1",
                Asset = "USDC",
                Resource = $"http://localhost{path}",
                MimeType = "application/json",
                PayTo = "0x0000000000000000000000000000000000000001",
                Description = "unit test"
            };
        }

        private static string CreateHeaderJson(string? resource = null, string? from = null, string? network = "base-sepolia", string to = "0x0000000000000000000000000000000000000001", string value = "1")
        {
            var payload = new
            {
                x402Version = 1,
                scheme = "exact",
                network,
                payload = new Dictionary<string, object?>
                {
                    { "authorization", new Dictionary<string, object?> {
                        { "from", from ?? "0xF00" },
                        { "to", to } ,
                        { "value", value }
                    } },
                    { "resource", $"http://localhost{resource}" }
                }
            };
            return JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        private static string CreateHeaderB64(string? resource = null, string? from = null, string? network = "base-sepolia")
        {
            string json = CreateHeaderJson(resource, from, network);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        [Test]
        public async Task NoHeader_Returns402_AndCannotContinue()
        {
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements("/api");
            using var host = BuildHost(facilitator, "/api", reqs);
            var client = host.GetTestClient();

            var resp = await client.GetAsync("/api");

            Assert.That(resp.StatusCode, Is.EqualTo((System.Net.HttpStatusCode)StatusCodes.Status402PaymentRequired));
        }

        [Test]
        public async Task MalformedHeader_Returns402()
        {
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements("/res");
            using var host = BuildHost(facilitator, "/res", reqs);
            var client = host.GetTestClient();
            var request = new HttpRequestMessage(HttpMethod.Get, "/res");
            request.Headers.Add("X-PAYMENT", "not-base64");

            var resp = await client.SendAsync(request);
            var content = await resp.Content.ReadAsStringAsync();

            Assert.That(resp.StatusCode, Is.EqualTo((System.Net.HttpStatusCode)StatusCodes.Status402PaymentRequired));
        }

        [Test]
        public async Task ResourceMismatch_Returns402()
        {
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements("/expected");
            using var host = BuildHost(facilitator, "/expected", reqs);
            var client = host.GetTestClient();
            var request = new HttpRequestMessage(HttpMethod.Get, "/expected");
            request.Headers.Add("X-PAYMENT", CreateHeaderB64(resource: "/different"));

            var resp = await client.SendAsync(request);

            Assert.That(resp.StatusCode, Is.EqualTo((System.Net.HttpStatusCode)StatusCodes.Status402PaymentRequired));
        }

        [Test]
        public async Task InvalidVerification_Returns402()
        {
            var facilitator = new FakeFacilitatorClient
            {
                VerifyAsyncImpl = (_, _) => Task.FromResult(new VerificationResponse { IsValid = false, InvalidReason = "bad" })
            };
            var reqs = CreateRequirements("/r");
            using var host = BuildHost(facilitator, "/r", reqs);
            var client = host.GetTestClient();
            var request = new HttpRequestMessage(HttpMethod.Get, "/r");
            request.Headers.Add("X-PAYMENT", CreateHeaderB64(resource: "/r"));

            var resp = await client.SendAsync(request);

            Assert.That(resp.StatusCode, Is.EqualTo((System.Net.HttpStatusCode)StatusCodes.Status402PaymentRequired));
        }

        [Test]
        public async Task Optimistic_SettlementSuccess_AddsHeader_AndContinue()
        {
            var facilitator = new FakeFacilitatorClient
            {
                VerifyAsyncImpl = (_, _) => Task.FromResult(new VerificationResponse { IsValid = true }),
                SettleAsyncImpl = (_, req) => Task.FromResult(new SettlementResponse { Success = true, Transaction = "0xdead", Network = req.Network })
            };
            var reqs = CreateRequirements("/ok");
            using var host = BuildHost(facilitator, "/ok", reqs, SettlementMode.Optimistic);
            var client = host.GetTestClient();
            var request = new HttpRequestMessage(HttpMethod.Get, "/ok");
            request.Headers.Add("X-PAYMENT", CreateHeaderB64(resource: "/ok", from: "0xabc"));

            var resp = await client.SendAsync(request);

            Assert.That(resp.IsSuccessStatusCode, Is.True);
            Assert.That(resp.Headers.Contains("X-PAYMENT-RESPONSE"), Is.True);
            Assert.That(string.Join(',', resp.Headers.GetValues("Access-Control-Expose-Headers")), Does.Contain("X-PAYMENT-RESPONSE"));
        }

        [Test]
        public async Task Optimistic_SettlementFailure_OnRequest_Writes200()
        {
            var facilitator = new FakeFacilitatorClient
            {
                VerifyAsyncImpl = (_, _) => Task.FromResult(new VerificationResponse { IsValid = true }),
                SettleAsyncImpl = (_, _) => Task.FromResult(new SettlementResponse { Success = false, ErrorReason = "settle failed" })
            };
            var reqs = CreateRequirements("/fail");
            using var host = BuildHost(facilitator, "/fail", reqs, SettlementMode.Optimistic);
            var client = host.GetTestClient();
            var request = new HttpRequestMessage(HttpMethod.Get, "/fail");
            request.Headers.Add("X-PAYMENT", CreateHeaderB64(resource: "/fail"));

            var resp = await client.SendAsync(request);
            Assert.That(resp.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
        }

        [Test]
        public async Task Pessimistic_SettlementFailure_Returns402_AndCannotContinue()
        {
            var facilitator = new FakeFacilitatorClient
            {
                VerifyAsyncImpl = (_, _) => Task.FromResult(new VerificationResponse { IsValid = true }),
                SettleAsyncImpl = (_, _) => Task.FromResult(new SettlementResponse { Success = false, ErrorReason = "not enough" })
            };
            var reqs = CreateRequirements("/pess-fail");
            using var host = BuildHost(facilitator, "/pess-fail", reqs, SettlementMode.Pessimistic);
            var client = host.GetTestClient();
            var request = new HttpRequestMessage(HttpMethod.Get, "/pess-fail");
            request.Headers.Add("X-PAYMENT", CreateHeaderB64(resource: "/pess-fail"));

            var resp = await client.SendAsync(request);

            Assert.That(resp.StatusCode, Is.EqualTo((System.Net.HttpStatusCode)StatusCodes.Status402PaymentRequired));
        }

        [Test]
        public async Task Pessimistic_SettlementSuccess_AddsHeaderAndContinue()
        {
            bool callbackCalled = false;
            var facilitator = new FakeFacilitatorClient
            {
                VerifyAsyncImpl = (_, _) => Task.FromResult(new VerificationResponse { IsValid = true }),
                SettleAsyncImpl = (_, req) => Task.FromResult(new SettlementResponse { Success = true, Transaction = "0xbeef", Network = req.Network })
            };
            var reqs = CreateRequirements("/pess-ok");
            using var host = BuildHost(
                facilitator,
                "/pess-ok",
                reqs,
                SettlementMode.Pessimistic,
                onSettlement: (ctx, sr, ex) => { callbackCalled = true; return Task.CompletedTask; });
            var client = host.GetTestClient();
            var request = new HttpRequestMessage(HttpMethod.Get, "/pess-ok");
            request.Headers.Add("X-PAYMENT", CreateHeaderB64(resource: "/pess-ok", from: "0xabc"));

            var resp = await client.SendAsync(request);

            Assert.That(resp.IsSuccessStatusCode, Is.True);
            Assert.That(callbackCalled, Is.True);
            Assert.That(resp.Headers.Contains("X-PAYMENT-RESPONSE"), Is.True);
        }
    }
}


