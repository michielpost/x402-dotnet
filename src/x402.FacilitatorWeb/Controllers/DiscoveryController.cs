using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using x402.Core.Models.v2.Facilitator;

namespace x402.FacilitatorWeb.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DiscoveryController : ControllerBase
    {
        private static readonly string DefaultPayTo = "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37";

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
                Items = new() { SampleItem() }
            };
        }

        [HttpGet]
        [Route("merchant")]
        [SwaggerIgnore]
        public MerchantDiscoveryResponse Merchant([FromQuery] string payTo, [FromQuery] int limit = 20, [FromQuery] int offset = 0)
        {
            var isKnownMerchant = string.Equals(payTo, DefaultPayTo, StringComparison.OrdinalIgnoreCase);
            return new()
            {
                PayTo = payTo,
                Pagination = new()
                {
                    Limit = limit,
                    Offset = offset,
                    Total = isKnownMerchant ? 1 : 0
                },
                Resources = isKnownMerchant ? new() { SampleItem() } : new()
            };
        }

        [HttpGet]
        [Route("search")]
        [SwaggerIgnore]
        public DiscoverySearchResponse Search(
            [FromQuery] string? query = null,
            [FromQuery] string? network = null,
            [FromQuery] string? asset = null,
            [FromQuery] string? scheme = null,
            [FromQuery] string? payTo = null,
            [FromQuery] string? urlSubstring = null,
            [FromQuery] string? maxUsdPrice = null,
            [FromQuery] List<string>? extensions = null,
            [FromQuery] int limit = 20)
        {
            return new()
            {
                Resources = new() { SampleItem() },
                PartialResults = false,
                SearchMethod = "text"
            };
        }

        private static DiscoveryItem SampleItem() => new()
        {
            LastUpdated = DateTimeOffset.UtcNow,
            Resource = "/resource/middleware",
            Type = "http",
            Description = "Sample x402 protected resource.",
            ServiceName = "Facilitator Web",
            Tags = new List<string> { "sample" },
            IconUrl = "https://example.com/icon.png",
            Accepts = new List<DiscoveryPaymentRequirements>
            {
                new DiscoveryPaymentRequirements
                {
                    Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
                    Amount = "1000",
                    Network = "eip155:84532",
                    PayTo = DefaultPayTo
                }
            }
        };
    }
}
