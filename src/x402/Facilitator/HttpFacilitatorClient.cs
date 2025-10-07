using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using x402.Facilitator.Models;
using x402.Models;

namespace x402.Facilitator
{
    public class HttpFacilitatorClient : IFacilitatorClient
    {
        private readonly HttpClient httpClient;
        private readonly ILogger<HttpFacilitatorClient> logger;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Creates a new HTTP facilitator client.
        /// </summary>
        /// <param name="baseUrl">The base URL of the facilitator service (trailing slash will be removed)</param>
        public HttpFacilitatorClient(HttpClient httpClient, ILogger<HttpFacilitatorClient> logger)
        {
            this.httpClient = httpClient;
            this.logger = logger;
        }


        public async Task<VerificationResponse> VerifyAsync(PaymentPayloadHeader paymentPayload, PaymentRequirements req)
        {
            logger.LogInformation("Verifying payment payload for resource {Resource} with scheme {Scheme} and asset {Asset}", req.Resource, req.Scheme, req.Asset);
            var body = new FacilitatorRequest
            {
                X402Version = 1,
                PaymentPayload = paymentPayload,
                PaymentRequirements = req
            };

            var response = await httpClient.PostAsJsonAsync($"/verify", body);
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                logger.LogWarning("Verification request failed with status {StatusCode}: {Content}", (int)response.StatusCode, content);
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {content}");
            }

            var result = await response.Content.ReadFromJsonAsync<VerificationResponse>(JsonOptions);
            if (result is null)
            {
                logger.LogError("Failed to deserialize verification response for resource {Resource}", req.Resource);
                throw new InvalidOperationException("Failed to deserialize verification response");
            }
            logger.LogInformation("Verification result for resource {Resource}: IsValid={IsValid} Reason={Reason}", req.Resource, result.IsValid, result.InvalidReason);
            return result;
        }

        public async Task<SettlementResponse> SettleAsync(PaymentPayloadHeader paymentPayload, PaymentRequirements req)
        {
            logger.LogInformation("Settling payment for resource {Resource} on network {Network} to {PayTo}", req.Resource, req.Network, req.PayTo);
            var body = new FacilitatorRequest
            {
                X402Version = 1,
                PaymentPayload = paymentPayload,
                PaymentRequirements = req
            };

            var response = await httpClient.PostAsJsonAsync($"/settle", body);
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                logger.LogWarning("Settlement request failed with status {StatusCode}: {Content}", (int)response.StatusCode, content);
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {content}");
            }

            var result = await response.Content.ReadFromJsonAsync<SettlementResponse>(JsonOptions);
            if (result is null)
            {
                logger.LogError("Failed to deserialize settlement response for resource {Resource}", req.Resource);
                throw new InvalidOperationException("Failed to deserialize settlement response");
            }
            logger.LogInformation("Settlement result for resource {Resource}: Success={Success} Tx={Tx}", req.Resource, result.Success, result.Transaction);
            return result;

        }

        public async Task<List<FacilitatorKind>> SupportedAsync()
        {
            logger.LogDebug("Requesting supported facilitator kinds");
            using var response = await httpClient.GetAsync($"/supported");

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                logger.LogWarning("Supported kinds request failed with status {StatusCode}: {Content}", (int)response.StatusCode, content);
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {content}");
            }

            var map = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(JsonOptions);

            if (map is null || !map.TryGetValue("kinds", out var kindsObj))
            {
                return new();
            }

            // Re-serialize then deserialize properly as List<Kind>
            var kindsJson = JsonSerializer.Serialize(kindsObj, JsonOptions);
            var kinds = JsonSerializer.Deserialize<List<FacilitatorKind>>(kindsJson, JsonOptions) ?? new List<FacilitatorKind>();
            logger.LogInformation("Retrieved {Count} supported facilitator kinds", kinds.Count);
            return kinds;
        }
    }
}
