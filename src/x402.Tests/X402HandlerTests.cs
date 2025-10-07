using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using x402.Enums;
using x402.Facilitator;
using x402.Facilitator.Models;
using x402.Models;

namespace x402.Tests
{
    [TestFixture]
    public class X402HandlerTests
    {
        private sealed class FakeFacilitatorClient : IFacilitatorClient
        {
            public Func<PaymentPayloadHeader, PaymentRequirements, Task<VerificationResponse>>? VerifyAsyncImpl { get; set; }
            public Func<PaymentPayloadHeader, PaymentRequirements, Task<SettlementResponse>>? SettleAsyncImpl { get; set; }

            public Task<VerificationResponse> VerifyAsync(PaymentPayloadHeader paymentPayload, PaymentRequirements requirements)
            {
                if (VerifyAsyncImpl != null) return VerifyAsyncImpl(paymentPayload, requirements);
                return Task.FromResult(new VerificationResponse { IsValid = true });
            }

            public Task<SettlementResponse> SettleAsync(PaymentPayloadHeader paymentPayload, PaymentRequirements requirements)
            {
                if (SettleAsyncImpl != null) return SettleAsyncImpl(paymentPayload, requirements);
                return Task.FromResult(new SettlementResponse { Success = true, Transaction = "0xabc", Network = requirements.Network });
            }

            public Task<List<FacilitatorKind>> SupportedAsync()
            {
                return Task.FromResult(new List<FacilitatorKind>());
            }
        }

        private static DefaultHttpContext CreateHttpContext(out ServiceProvider sp)
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddDebug().AddConsole().SetMinimumLevel(LogLevel.Debug));
            sp = services.BuildServiceProvider();

            var context = new DefaultHttpContext
            {
                RequestServices = sp
            };
            context.Response.Body = new MemoryStream();
            return context;
        }

        private static PaymentRequirements CreateRequirements(string path)
        {
            return new PaymentRequirements
            {
                Scheme = PaymentScheme.Exact,
                Network = "base-sepolia",
                MaxAmountRequired = "1",
                Asset = "USDC",
                Resource = path,
                MimeType = "application/json",
                PayTo = "0x0000000000000000000000000000000000000001",
                Description = "unit test"
            };
        }

        private static string CreateHeaderJson(string? resource = null, string? from = null, string? network = "base-sepolia")
        {
            var payload = new
            {
                x402Version = 1,
                scheme = "exact",
                network,
                payload = new Dictionary<string, object?>
                {
                    { "authorization", new Dictionary<string, object?> { { "from", from ?? "0xF00" } } },
                    { "resource", resource }
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
            var context = CreateHttpContext(out var sp);
            context.Request.Path = "/api";
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements("/api");

            var result = await X402Handler.HandleX402Async(context, facilitator, "/api", reqs);

            Assert.That(result.CanContinueRequest, Is.False);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status402PaymentRequired));
        }

        [Test]
        public async Task MalformedHeader_Returns402()
        {
            var context = CreateHttpContext(out var sp);
            context.Request.Path = "/res";
            context.Request.Headers["X-PAYMENT"] = "not-base64";
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements("/res");

            var result = await X402Handler.HandleX402Async(context, facilitator, "/res", reqs);

            Assert.That(result.CanContinueRequest, Is.False);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status402PaymentRequired));
        }

        [Test]
        public async Task ResourceMismatch_Returns402()
        {
            var context = CreateHttpContext(out var sp);
            context.Request.Path = "/expected";
            context.Request.Headers["X-PAYMENT"] = CreateHeaderB64(resource: "/different");
            var facilitator = new FakeFacilitatorClient();
            var reqs = CreateRequirements("/expected");

            var result = await X402Handler.HandleX402Async(context, facilitator, "/expected", reqs);

            Assert.That(result.CanContinueRequest, Is.False);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status402PaymentRequired));
        }

        [Test]
        public async Task InvalidVerification_Returns402()
        {
            var context = CreateHttpContext(out var sp);
            context.Request.Path = "/r";
            context.Request.Headers["X-PAYMENT"] = CreateHeaderB64(resource: "/r");
            var facilitator = new FakeFacilitatorClient
            {
                VerifyAsyncImpl = (_, _) => Task.FromResult(new VerificationResponse { IsValid = false, InvalidReason = "bad" })
            };
            var reqs = CreateRequirements("/r");

            var result = await X402Handler.HandleX402Async(context, facilitator, "/r", reqs);

            Assert.That(result.CanContinueRequest, Is.False);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status402PaymentRequired));
        }

        [Test]
        public async Task Optimistic_SettlementSuccess_AddsHeader_AndContinue()
        {
            var context = CreateHttpContext(out var sp);
            context.Request.Path = "/ok";
            context.Request.Headers["X-PAYMENT"] = CreateHeaderB64(resource: "/ok", from: "0xabc");
            var facilitator = new FakeFacilitatorClient
            {
                VerifyAsyncImpl = (_, _) => Task.FromResult(new VerificationResponse { IsValid = true }),
                SettleAsyncImpl = (_, req) => Task.FromResult(new SettlementResponse { Success = true, Transaction = "0xdead", Network = req.Network })
            };
            var reqs = CreateRequirements("/ok");

            var result = await X402Handler.HandleX402Async(context, facilitator, "/ok", reqs, SettlementMode.Optimistic);
            Assert.That(result.CanContinueRequest, Is.True);

            await context.Response.StartAsync();
            Assert.That(context.Response.Headers.ContainsKey("X-PAYMENT-RESPONSE"), Is.True);
            Assert.That(context.Response.Headers["Access-Control-Expose-Headers"].ToString(), Does.Contain("X-PAYMENT-RESPONSE"));
        }

        [Test]
        public async Task Optimistic_SettlementFailure_OnStarting_Writes200()
        {
            var context = CreateHttpContext(out var sp);
            context.Request.Path = "/fail";
            context.Request.Headers["X-PAYMENT"] = CreateHeaderB64(resource: "/fail");
            var facilitator = new FakeFacilitatorClient
            {
                VerifyAsyncImpl = (_, _) => Task.FromResult(new VerificationResponse { IsValid = true }),
                SettleAsyncImpl = (_, _) => Task.FromResult(new SettlementResponse { Success = false, ErrorReason = "settle failed" })
            };
            var reqs = CreateRequirements("/fail");

            var result = await X402Handler.HandleX402Async(context, facilitator, "/fail", reqs, SettlementMode.Optimistic);
            Assert.That(result.CanContinueRequest, Is.True);

            await context.Response.StartAsync();
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        }

        [Test]
        public async Task Pessimistic_SettlementFailure_Returns402_AndCannotContinue()
        {
            var context = CreateHttpContext(out var sp);
            context.Request.Path = "/pess-fail";
            context.Request.Headers["X-PAYMENT"] = CreateHeaderB64(resource: "/pess-fail");
            var facilitator = new FakeFacilitatorClient
            {
                VerifyAsyncImpl = (_, _) => Task.FromResult(new VerificationResponse { IsValid = true }),
                SettleAsyncImpl = (_, _) => Task.FromResult(new SettlementResponse { Success = false, ErrorReason = "not enough" })
            };
            var reqs = CreateRequirements("/pess-fail");

            var result = await X402Handler.HandleX402Async(context, facilitator, "/pess-fail", reqs, SettlementMode.Pessimistic);

            Assert.That(result.CanContinueRequest, Is.False);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status402PaymentRequired));
        }

        [Test]
        public async Task Pessimistic_SettlementSuccess_AddsHeaderAndContinue()
        {
            var context = CreateHttpContext(out var sp);
            context.Request.Path = "/pess-ok";
            context.Request.Headers["X-PAYMENT"] = CreateHeaderB64(resource: "/pess-ok", from: "0xabc");
            bool callbackCalled = false;
            var facilitator = new FakeFacilitatorClient
            {
                VerifyAsyncImpl = (_, _) => Task.FromResult(new VerificationResponse { IsValid = true }),
                SettleAsyncImpl = (_, req) => Task.FromResult(new SettlementResponse { Success = true, Transaction = "0xbeef", Network = req.Network })
            };
            var reqs = CreateRequirements("/pess-ok");

            var result = await X402Handler.HandleX402Async(context, facilitator, "/pess-ok", reqs, SettlementMode.Pessimistic, onSettlement: (ctx, sr) => { callbackCalled = true; return Task.CompletedTask; });
            Assert.That(result.CanContinueRequest, Is.True);
            Assert.That(callbackCalled, Is.True);

            await context.Response.StartAsync();
            Assert.That(context.Response.Headers.ContainsKey("X-PAYMENT-RESPONSE"), Is.True);
        }
    }
}


