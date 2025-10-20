using Microsoft.AspNetCore.Mvc;
using x402.Attributes;
using x402.Core.Enums;
using x402.Core.Models;
using x402.Core.Models.Facilitator;
using x402.Facilitator;
using x402.SampleWeb.Models;

namespace x402.SampleWeb.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ResourceController : ControllerBase
    {
        private readonly IFacilitatorClient facilitator;

        public ResourceController(IFacilitatorClient facilitator)
        {
            this.facilitator = facilitator;
        }

        /// <summary>
        /// Protected by Middleware
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("middleware")]
        public SampleResult Middleware()
        {
            return new SampleResult { Title = "Protected by middleware" };
        }

        [HttpGet]
        [Route("free")]
        public SampleResult Free()
        {
            return new SampleResult { Title = "Free Resource" };
        }

        [HttpGet]
        [Route("protected")]
        [PaymentRequired("1000", "0x036CbD53842c5426634e7929541eC2318f3dCF7e", "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37", "base-sepolia")]
        public SampleResult Protected()
        {
            return new SampleResult { Title = "Protected by PaymentRequired Attribute" };
        }

        [HttpPost]
        [Route("protected")]
        [PaymentRequired("1000", "0x036CbD53842c5426634e7929541eC2318f3dCF7e", "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37", "base-sepolia")]
        public SampleResult ProtectedPost([FromBody] SampleRequest req)
        {
            return new SampleResult { Title = "Protected by PaymentRequired Attribute" };
        }

        [HttpGet]
        [Route("dynamic")]
        public async Task<SampleResult?> Dynamic(string amount)
        {
            var request = this.HttpContext.Request;
            var fullUrl = $"{request.Scheme}://{request.Host}{request.Path}{request.QueryString}";

            var x402Result = await X402Handler.HandleX402Async(this.HttpContext, facilitator, fullUrl,
                new PaymentRequirements
                {
                    Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
                    Description = "Dynamic payment",
                    Network = "base-sepolia",
                    MaxAmountRequired = amount,
                    PayTo = "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37",
                    Resource = fullUrl,
                },
                SettlementMode.Optimistic,
                OnSettlement);

            if (!x402Result.CanContinueRequest)
            {
                return null;
            }

            return new SampleResult { Title = $"Dynamic protected for {amount}, paid by: {x402Result.VerificationResponse?.Payer}" };
        }

        private Task OnSettlement(HttpContext context, SettlementResponse response)
        {
            Console.WriteLine("Settlement completed: " + response.Success);

            return Task.CompletedTask;
        }

    }
}
