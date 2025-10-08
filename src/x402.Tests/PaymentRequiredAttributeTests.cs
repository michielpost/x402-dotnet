using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using x402.Attributes;
using x402.Enums;
using x402.Facilitator;
using x402.Facilitator.Models;
using x402.Models;

namespace x402.Tests
{
    [TestFixture]
    public class PaymentRequiredAttributeTests
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

        private static ActionExecutingContext CreateActionExecutingContext(IServiceProvider services, string path = "/pay")
        {
            var httpContext = new DefaultHttpContext();
            httpContext.RequestServices = services;
            httpContext.Request.Path = path;
            var routeData = new RouteData();
            var actionContext = new ActionContext(httpContext, routeData, new ControllerActionDescriptor());
            var filters = new List<IFilterMetadata>();
            var actionArguments = new Dictionary<string, object?>();
            return new ActionExecutingContext(actionContext, filters, actionArguments, controller: new object());
        }

        [Test]
        public async Task NoHeader_StopsExecution_Returns402()
        {
            bool nextCalled = false;

            var services = new ServiceCollection()
                .AddLogging(b => b.AddDebug().AddConsole().SetMinimumLevel(LogLevel.Debug))
                .AddSingleton<IFacilitatorClient, FakeFacilitatorClient>()
                .BuildServiceProvider();

            var context = CreateActionExecutingContext(services, "/needs-pay");

            var attr = new PaymentRequiredAttribute(
                maxAmountRequired: "1",
                asset: "USDC",
                payTo: "0x0000000000000000000000000000000000000001",
                network: "base-sepolia",
                scheme: PaymentScheme.Exact)
            {
                Description = "unit test",
                MimeType = "application/json",
                SettlementMode = SettlementMode.Optimistic
            };

            await attr.OnActionExecutionAsync(context, () =>
            {
                nextCalled = true;
                return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), controller: new object()));
            });

            Assert.That(nextCalled, Is.False);
            Assert.That(context.HttpContext.Response.StatusCode, Is.EqualTo(StatusCodes.Status402PaymentRequired));
        }

        [Test]
        public async Task ValidPayment_ContinuesExecution()
        {
            bool nextCalled = false;
            var fake = new FakeFacilitatorClient
            {
                VerifyAsyncImpl = (_, _) => Task.FromResult(new VerificationResponse { IsValid = true }),
                SettleAsyncImpl = (_, req) => Task.FromResult(new SettlementResponse { Success = true, Transaction = "0xdead", Network = req.Network })
            };

            var services = new ServiceCollection()
                .AddLogging(b => b.AddDebug().AddConsole().SetMinimumLevel(LogLevel.Debug))
                .AddSingleton<IFacilitatorClient>(fake)
                .BuildServiceProvider();

            var context = CreateActionExecutingContext(services, "/ok");

            // Add a valid X-PAYMENT header with resource match
            var headerJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                x402Version = 1,
                scheme = "exact",
                network = "base-sepolia",
                payload = new Dictionary<string, object?>
                {
                    { "authorization", new Dictionary<string, object?> { { "from", "0xabc" } } },
                    { "resource", ":///ok" }
                }
            }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            var headerB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(headerJson));
            context.HttpContext.Request.Headers["X-PAYMENT"] = headerB64;

            var attr = new PaymentRequiredAttribute(
                maxAmountRequired: "1",
                asset: "USDC",
                payTo: "0x0000000000000000000000000000000000000001",
                network: "base-sepolia",
                scheme: PaymentScheme.Exact)
            {
                Description = "unit test",
                MimeType = "application/json",
                SettlementMode = SettlementMode.Optimistic
            };

            await attr.OnActionExecutionAsync(context, () =>
            {
                nextCalled = true;
                return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), controller: new object()));
            });

            Assert.That(nextCalled, Is.True);
            Assert.That(context.HttpContext.Response.HasStarted || context.HttpContext.Response.StatusCode == 0 || context.HttpContext.Response.StatusCode == StatusCodes.Status200OK, Is.True);
        }
    }
}


