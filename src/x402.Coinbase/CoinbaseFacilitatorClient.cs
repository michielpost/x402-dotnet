using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using x402.Coinbase.Models;
using x402.Facilitator;
using x402.Facilitator.Models;
using x402.Models;

namespace x402.Coinbase
{
    /// <summary>
    /// https://docs.cdp.coinbase.com/api-reference/v2/rest-api/x402-facilitator/x402-facilitator
    /// </summary>
    public class CoinbaseFacilitatorClient : IFacilitatorClient
    {
        private readonly CoinbaseOptions coinbaseOptions;
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
        public CoinbaseFacilitatorClient(IHttpClientFactory httpClientFactory, IOptions<CoinbaseOptions> coinbaseOptions)
        {
            this.httpClientFactory = httpClientFactory;
            this.coinbaseOptions = coinbaseOptions.Value;

            //Remove trailing slash
            this.coinbaseOptions.BaseUrl = this.coinbaseOptions.BaseUrl.EndsWith("/") ? this.coinbaseOptions.BaseUrl[..^1] : this.coinbaseOptions.BaseUrl;
        }


        public async Task<VerificationResponse> VerifyAsync(PaymentPayloadHeader paymentPayload, PaymentRequirements req)
        {
            var body = new FacilitatorRequest
            {
                X402Version = 1,
                PaymentPayload = paymentPayload,
                PaymentRequirements = req
            };

            var url = $"{coinbaseOptions.BaseUrl}/verify";
            string accessToken = JWTHelper.GenerateBearerJWT(coinbaseOptions.ApiKeyId, coinbaseOptions.ApiKeySecret, "POST", url);

            var httpClient = httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await httpClient.PostAsJsonAsync(url, body);
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

            var url = $"{coinbaseOptions.BaseUrl}/settle";
            string accessToken = JWTHelper.GenerateBearerJWT(coinbaseOptions.ApiKeyId, coinbaseOptions.ApiKeySecret, "POST", url);

            var httpClient = httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await httpClient.PostAsJsonAsync(url, body);
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
            var url = $"{coinbaseOptions.BaseUrl}/supported";
            string accessToken = JWTHelper.GenerateBearerJWT(coinbaseOptions.ApiKeyId, coinbaseOptions.ApiKeySecret, "GET", url);

            var httpClient = httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await httpClient.GetAsync(url);

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
