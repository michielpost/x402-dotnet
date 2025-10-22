using Microsoft.AspNetCore.Mvc;
using x402.Attributes;
using x402.Core.Enums;
using x402.Core.Models;
using x402.SampleWeb.Models;

namespace x402.SampleWeb.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ResourceController : ControllerBase
    {
        private readonly X402Handler x402Handler;

        public ResourceController(X402Handler x402Handler)
        {
            this.x402Handler = x402Handler;
        }

        /// <summary>
        /// Protected by Middleware
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("middleware")]
        public SampleResult Middleware()
        {
            return new SampleResult { Title = "Success! Protected by middleware" };
        }

        [HttpGet]
        [Route("free")]
        public SampleResult Free()
        {
            return new SampleResult { Title = "Free Resource" };
        }

        [HttpGet]
        [Route("protected")]
        [PaymentRequired("1000", "0x036CbD53842c5426634e7929541eC2318f3dCF7e", "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37", Discoverable = true, SettlementMode = SettlementMode.Pessimistic)]
        public SampleResult Protected()
        {
            return new SampleResult { Title = "Success! Protected by PaymentRequired Attribute" };
        }

        [HttpPost]
        [Route("protected")]
        [PaymentRequired("1000", "0x036CbD53842c5426634e7929541eC2318f3dCF7e", "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37", Discoverable = true)]
        public SampleResult ProtectedPost([FromBody] SampleRequest req)
        {
            return new SampleResult { Title = "Success! Protected by PaymentRequired Attribute" };
        }

        [HttpGet]
        [Route("dynamic")]
        public async Task<SampleResult?> Dynamic(string amount)
        {
            var x402Result = await x402Handler.HandleX402Async(
                new PaymentRequirementsBasic
                {
                    Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
                    Description = "Dynamic payment",
                    MaxAmountRequired = amount,
                    PayTo = "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37",
                },
                discoverable: true,
                SettlementMode.Pessimistic,
                onSettlement: (context, response) =>
                {
                    Console.WriteLine("Settlement completed: " + response.Success);
                    return Task.CompletedTask;
                });

            if (!x402Result.CanContinueRequest)
            {
                return null;
            }

            return new SampleResult { Title = $"Success! Dynamic protected for {amount}, paid by: {x402Result.VerificationResponse?.Payer}" };
        }

        [HttpPost]
        [Route("send-msg")]
        public async Task<SampleResult?> SendMsg([FromBody] SampleRequest req)
        {
            var x402Result = await x402Handler.HandleX402Async(
                new PaymentRequirementsBasic
                {
                    Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
                    Description = "Send a message",
                    MaxAmountRequired = "1000",
                    PayTo = "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37",
                },
                discoverable: true,
                SettlementMode.Pessimistic,
                onSetOutputSchema: (context, reqs, schema) =>
                {
                    schema.Input ??= new();

                    //Manually set the input schema
                    schema.Input.BodyFields = new Dictionary<string, object>
                    {
                        {
                            nameof(req.Value),
                            new FieldDefenition
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

            return new SampleResult { Title = $"Success! Msg: {req.Value}, paid by: {x402Result.VerificationResponse?.Payer}" };
        }

    }
}
