using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using x402.Core.Models.v2.Facilitator;
using x402.Facilitator;

namespace x402.Tests
{
    [TestFixture]
    public class HttpFacilitatorClientDiscoveryTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            public HttpRequestMessage? LastRequest { get; private set; }
            public string ResponseJson { get; set; } = "{}";

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ResponseJson, Encoding.UTF8, "application/json")
                });
            }
        }

        private static (HttpFacilitatorClient client, StubHandler handler) CreateClient(string responseJson)
        {
            var handler = new StubHandler { ResponseJson = responseJson };
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://facilitator.example/") };
            return (new HttpFacilitatorClient(httpClient, new NullLogger<HttpFacilitatorClient>()), handler);
        }

        private const string ItemJson = """
            {
                "resource": "https://api.example.com/weather",
                "description": "Real-time weather forecast data.",
                "type": "http",
                "x402Version": 2,
                "lastUpdated": "2024-01-15T10:30:00Z",
                "accepts": [
                    {
                        "scheme": "exact",
                        "network": "eip155:8453",
                        "amount": "1000000",
                        "payTo": "0x742d35Cc6634C0532925a3b844Bc454e4438f44e",
                        "asset": "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
                        "maxTimeoutSeconds": 60
                    }
                ],
                "extensions": {
                    "bazaar": { "info": { "input": { "type": "http", "method": "GET" } }, "schema": {} }
                },
                "quality": {
                    "l30DaysTotalCalls": 42,
                    "l30DaysUniquePayers": 15,
                    "lastCalledAt": "2024-01-15T10:30:00Z"
                },
                "serviceName": "Weather API",
                "tags": ["weather", "data"],
                "iconUrl": "https://cdn.example.com/icon.png"
            }
            """;

        [Test]
        public async Task DiscoveryAsync_BuildsUrl_AndDeserializesMetadata()
        {
            var json = $$$"""{"x402Version":2,"items":[{{{ItemJson}}}],"pagination":{"limit":20,"offset":0,"total":1}}""";
            var (client, handler) = CreateClient(json);

            var result = await client.DiscoveryAsync(type: "http", limit: 20, offset: 5);

            Assert.That(handler.LastRequest!.RequestUri!.ToString(),
                Is.EqualTo("https://facilitator.example/discovery/resources?type=http&limit=20&offset=5"));

            var item = result.Items.Single();
            Assert.That(item.ServiceName, Is.EqualTo("Weather API"));
            Assert.That(item.Tags, Is.EqualTo(new[] { "weather", "data" }));
            Assert.That(item.IconUrl, Is.EqualTo("https://cdn.example.com/icon.png"));
            Assert.That(item.Description, Is.EqualTo("Real-time weather forecast data."));
            Assert.That(item.Quality!.L30DaysTotalCalls, Is.EqualTo(42));
            Assert.That(item.Quality.L30DaysUniquePayers, Is.EqualTo(15));
            Assert.That(item.Extensions!.ContainsKey("bazaar"), Is.True);
        }

        [Test]
        public async Task DiscoveryMerchantAsync_BuildsUrl_AndDeserializesResources()
        {
            var payTo = "0x742d35Cc6634C0532925a3b844Bc454e4438f44e";
            var json = $$$"""{"x402Version":2,"payTo":"{{{payTo}}}","resources":[{{{ItemJson}}}],"pagination":{"limit":20,"offset":0,"total":1}}""";
            var (client, handler) = CreateClient(json);

            var result = await client.DiscoveryMerchantAsync(payTo, limit: 10, offset: 0);

            Assert.That(handler.LastRequest!.RequestUri!.ToString(),
                Is.EqualTo($"https://facilitator.example/discovery/merchant?payTo={payTo}&limit=10&offset=0"));
            Assert.That(result.PayTo, Is.EqualTo(payTo));
            Assert.That(result.Resources.Single().ServiceName, Is.EqualTo("Weather API"));
            Assert.That(result.Pagination.Total, Is.EqualTo(1));
        }

        [Test]
        public void DiscoveryMerchantAsync_EmptyPayTo_Throws()
        {
            var (client, _) = CreateClient("{}");
            Assert.ThrowsAsync<ArgumentException>(() => client.DiscoveryMerchantAsync(" "));
        }

        [Test]
        public async Task DiscoverySearchAsync_BuildsUrl_AndDeserializesResults()
        {
            var json = $$"""{"x402Version":2,"resources":[{{ItemJson}}],"partialResults":true,"searchMethod":"hybrid"}""";
            var (client, handler) = CreateClient(json);

            var result = await client.DiscoverySearchAsync(new DiscoverySearchRequest
            {
                Query = "weather forecast",
                Network = "eip155:8453",
                Scheme = "exact",
                UrlSubstring = "api.example.com",
                MaxUsdPrice = "1.00",
                Extensions = new List<string> { "bazaar", "other" },
                Limit = 5
            });

            Assert.That(handler.LastRequest!.RequestUri!.AbsoluteUri,
                Is.EqualTo("https://facilitator.example/discovery/search" +
                    "?query=weather%20forecast&network=eip155%3A8453&scheme=exact" +
                    "&urlSubstring=api.example.com&maxUsdPrice=1.00" +
                    "&extensions=bazaar&extensions=other&limit=5"));

            Assert.That(result.PartialResults, Is.True);
            Assert.That(result.SearchMethod, Is.EqualTo("hybrid"));
            Assert.That(result.Resources.Single().Tags, Does.Contain("weather"));
        }
    }
}
