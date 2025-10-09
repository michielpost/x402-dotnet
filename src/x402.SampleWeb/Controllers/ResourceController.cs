using Microsoft.AspNetCore.Mvc;
using x402.Attributes;
using x402.Facilitator;
using x402.Facilitator.Models;
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
            return new SampleResult { Title = "Protected by middleware"};
        }

        [HttpGet]
        [Route("free")]
        public SampleResult Free()
        {
            return new SampleResult { Title = "Free Resource"};
        }

        [HttpGet]
        [Route("protected")]
        [PaymentRequired("1000", "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v", "corzHctjX9Wtcrkfxz3Se8zdXqJYCaamWcQA7vwKF7Q", "solana-mainnet-beta")]
        public SampleResult Protected()
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
                new x402.Models.PaymentRequirements
                {
                    Asset = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v",
                    Description = "Dynamic payment",
                    Network = "solana-mainnet-beta",
                    MaxAmountRequired = amount,
                    PayTo = "corzHctjX9Wtcrkfxz3Se8zdXqJYCaamWcQA7vwKF7Q",
                    Resource = fullUrl,
                },
                Enums.SettlementMode.Optimistic,
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
