using System.Net.Http.Json;
using System.Text.Json;
using x402.Facilitator.Models;
using x402.Models;

namespace x402.Facilitator
{
    public class HttpFacilitatorClient : IFacilitatorClient
    {
        private readonly string _baseUrl;
        private readonly IHttpClientFactory httpClientFactory;

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Creates a new HTTP facilitator client.
        /// </summary>
        /// <param name="baseUrl">The base URL of the facilitator service (trailing slash will be removed)</param>
        public HttpFacilitatorClient(IHttpClientFactory httpClientFactory, string baseUrl)
        {
            _baseUrl = baseUrl.EndsWith("/")
                ? baseUrl[..^1] // remove trailing slash
                : baseUrl;
            this.httpClientFactory = httpClientFactory;
        }


        public async Task<VerificationResponse> VerifyAsync(PaymentPayloadHeader paymentPayload, PaymentRequirements req)
        {
            var body = new FacilitatorRequest
            {
                X402Version = 1,
                PaymentPayload = paymentPayload,
                PaymentRequirements = req
            };

            var httpClient = httpClientFactory.CreateClient();
            var response = await httpClient.PostAsJsonAsync($"{_baseUrl}/verify", body);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            }

            var result = await response.Content.ReadFromJsonAsync<VerificationResponse>(JsonOptions);
            if (result is null)
            {
                throw new InvalidOperationException("Failed to deserialize verification response");
            }
            return result;
        }

        public async Task<SettlementResponse> SettleAsync(PaymentPayloadHeader paymentPayload, PaymentRequirements req)
        {
            var body = new FacilitatorRequest
            {
                X402Version = 1,
                PaymentPayload = paymentPayload,
                PaymentRequirements = req
            };

            var httpClient = httpClientFactory.CreateClient();
            var response = await httpClient.PostAsJsonAsync($"{_baseUrl}/settle", body);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            }

            var result = await response.Content.ReadFromJsonAsync<SettlementResponse>(JsonOptions);
            if (result is null)
            {
                throw new InvalidOperationException("Failed to deserialize settlement response");
            }
            return result;

        }

        public async Task<List<FacilitatorKind>> SupportedAsync()
        {
            var httpClient = httpClientFactory.CreateClient();
            using var response = await httpClient.GetAsync($"{_baseUrl}/supported");

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            }

            var map = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(JsonOptions);

            if (map is null || !map.TryGetValue("kinds", out var kindsObj))
            {
                return new();
            }

            // Re-serialize then deserialize properly as List<Kind>
            var kindsJson = JsonSerializer.Serialize(kindsObj, JsonOptions);
            var kinds = JsonSerializer.Deserialize<List<FacilitatorKind>>(kindsJson, JsonOptions) ?? new List<FacilitatorKind>();

            return kinds;
        }
    }
}
