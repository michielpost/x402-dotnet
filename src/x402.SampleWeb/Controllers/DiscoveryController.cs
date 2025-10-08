using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using x402.Facilitator;

namespace x402.SampleWeb.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DiscoveryController : ControllerBase
    {
        [HttpGet]
        [Route("resources")]
        [SwaggerIgnore]
        public DiscoveryResponse Discovery([FromQuery] string? type = null, [FromQuery] int limit = 20, [FromQuery] int offset = 0)
        {
            return new()
            {
                Pagination = new()
                {
                    Limit = limit,
                    Offset = offset,
                    Total = 1
                },
                Items = new()
            {
                new Item
                {
                    LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Resource = "/resource/middleware",
                    Type = "http",
                    Accepts = new List<x402.Models.PaymentRequirements>
                    {
                        new x402.Models.PaymentRequirements
                        {
                        Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
                        MaxAmountRequired = "1000",
                        Network = "base-sepolia",
                        PayTo = "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37"
                        }
                    }
                }
            }
            };
        }
    }
}
