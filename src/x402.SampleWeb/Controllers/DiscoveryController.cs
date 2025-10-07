using Microsoft.AspNetCore.Mvc;
using x402.Facilitator;

namespace x402.SampleWeb.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DiscoveryController : ControllerBase
    {
        [HttpGet]
        [Route("resources")]
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
                    Resource = "/",
                    Type = "http",
                     Accepts = new List<Models.PaymentRequirements>
                     {
                         new Models.PaymentRequirements
                         {
                            Asset = "USDC",
                            MaxAmountRequired = "1",
                            Network = "base-sepolia",
                            PayTo = "0xTODO"
                         }
                     }
                }
            }
            };
        }
    }
}
