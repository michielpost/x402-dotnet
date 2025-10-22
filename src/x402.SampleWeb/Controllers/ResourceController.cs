using Microsoft.AspNetCore.Mvc;
using x402.Attributes;
using x402.Core.Enums;
using x402.Core.Models;
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
        [PaymentRequired("1000", "0x036CbD53842c5426634e7929541eC2318f3dCF7e", "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37", "base-sepolia", Discoverable = true)]
        public SampleResult Protected()
        {
            return new SampleResult { Title = "Protected by PaymentRequired Attribute" };
        }

        [HttpPost]
        [Route("protected")]
        [PaymentRequired("1000", "0x036CbD53842c5426634e7929541eC2318f3dCF7e", "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37", "base-sepolia", Discoverable = true)]
        public SampleResult ProtectedPost([FromBody] SampleRequest req)
        {
            return new SampleResult { Title = "Protected by PaymentRequired Attribute" };
        }

        [HttpGet]
        [Route("dynamic")]
        public async Task<SampleResult?> Dynamic(string amount)
        {
            var x402Result = await X402Handler.HandleX402Async(this.HttpContext, facilitator,
                new PaymentRequirements
                {
                    Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
                    Description = "Dynamic payment",
                    Network = "base-sepolia",
                    MaxAmountRequired = amount,
                    PayTo = "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37",
                    Extra = new PaymentRequirementsExtra
                    {
                        Name = "USDC",
                        Version = "2"
                    }
                },
                discoverable: true,
                SettlementMode.Optimistic,
                onSettlement: (context, response) =>
                {
                    Console.WriteLine("Settlement completed: " + response.Success);
                    return Task.CompletedTask;
                });

            if (!x402Result.CanContinueRequest)
            {
                return null;
            }

            return new SampleResult { Title = $"Dynamic protected for {amount}, paid by: {x402Result.VerificationResponse?.Payer}" };
        }

        [HttpPost]
        [Route("send-msg")]
        public async Task<SampleResult?> SendMsg([FromBody] SampleRequest req)
        {
            var x402Result = await X402Handler.HandleX402Async(this.HttpContext, facilitator,
                new PaymentRequirements
                {
                    Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
                    Description = "Send a message",
                    Network = "base-sepolia",
                    MaxAmountRequired = "1000",
                    PayTo = "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37",
                    Extra = new PaymentRequirementsExtra
                    {
                        Name = "USDC",
                        Version = "2"
                    }
                },
                discoverable: true,
                SettlementMode.Optimistic,
                onSetOutputSchema: (context, reqs, schema) =>
                {
                    schema.Input ??= new();

                    //Manually set the input schema
                    schema.Input.BodyFields = new Dictionary<string, BodyFieldProps>
                    {
                        {
                            nameof(req.Value),
                            new BodyFieldProps
                            {
                                Required = true,
                                Description = "Message to send",
                                Type = "string"
                            }
                        }
                    };

                    return schema;
                });

            if (!x402Result.CanContinueRequest)
            {
                return null;
            }

            return new SampleResult { Title = $"Msg: {req.Value}, paid by: {x402Result.VerificationResponse?.Payer}" };
        }

    }
}
