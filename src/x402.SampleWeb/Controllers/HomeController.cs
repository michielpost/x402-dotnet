using Microsoft.AspNetCore.Mvc;
using System;
using x402.Attributes;
using x402.Facilitator;

namespace x402.SampleWeb.Controllers
{
    [Route("{controller}")]
    public class HomeController : Controller
    {
        private readonly IFacilitatorClient facilitator;

        public HomeController(IFacilitatorClient facilitator)
        {
            this.facilitator = facilitator;
        }

        /// <summary>
        /// Protected by Middleware
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("/")]
        public IActionResult Index()
        {
            return Content("Test");
        }

        [HttpGet]
        [Route("free")]
        public IActionResult Free()
        {
            return Content("Free");
        }

        [HttpGet]
        [Route("protected")]
        [PaymentRequired("1", "USDC", "0x", "base-sepolia")]
        public IActionResult Protected()
        {
            return Content("Protected");
        }

        [HttpGet]
        [Route("dynamic")]
        public async Task<IActionResult> Dynamic(string amount)
        {
            var request = this.HttpContext.Request;
            var fullUrl = $"{request.Scheme}://{request.Host}{request.Path}{request.QueryString}";

            var x402Result = await X402Handler.HandleX402Async(this.HttpContext, facilitator, fullUrl,
                new Models.PaymentRequirements
                {
                    Asset = "USDC",
                    Description = "Dynamic payment",
                    Network = "base-sepolia",
                    MaxAmountRequired = amount,
                    PayTo = "0x"
                });

            if (!x402Result.CanContinueRequest)
            {
                return new EmptyResult(); // Response already written by HandleX402Async, so just exit
            }


            return Content($"Dynamic protected for {amount}, paid by: {x402Result.VerificationResponse?.Payer}");
        }
    }
}
