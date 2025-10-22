using Microsoft.AspNetCore.Mvc;
using x402.Core.Enums;
using x402.Core.Models;
using x402.Facilitator;
using x402.SampleWeb.Models;

namespace x402.SampleWeb.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class MainnetResourceController : ControllerBase
    {
        private readonly IFacilitatorClient facilitator;
        private readonly X402Handler x402Handler;

        public MainnetResourceController(IFacilitatorClient facilitator, X402Handler x402Handler)
        {
            this.facilitator = facilitator;
            this.x402Handler = x402Handler;
        }


        [HttpPost]
        [Route("send-msg")]
        public async Task<SampleResult?> SendMsg([FromBody] SampleRequest req)
        {
            var x402Result = await x402Handler.HandleX402Async(
                new PaymentRequirementsBasic
                {
                    Asset = "0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913",
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
