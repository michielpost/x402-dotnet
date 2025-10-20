using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using x402.Core.Models;
using x402.Core.Models.Facilitator;

namespace x402.Facilitator
{
    public class HttpFacilitatorClient : IFacilitatorClient
    {
        private readonly HttpClient httpClient;
        private readonly ILogger<HttpFacilitatorClient> logger;
        protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
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

        /// <summary>
        /// Allows derived clients to adjust the URL for a given relative path and HTTP method.
        /// Default behavior is to return the relative path unchanged (uses HttpClient.BaseAddress).
        /// </summary>
        /// <param name="relativePath">A path like "/verify" or "/settle".</param>
        /// <param name="method">HTTP method for the request.</param>
        /// <returns>URL or relative path to pass to HttpRequestMessage.</returns>
        protected virtual string BuildUrl(string relativePath, HttpMethod method) => relativePath;

        /// <summary>
        /// Allows derived clients to modify the outgoing request (e.g., add headers).
        /// Default behavior is no-op.
        /// </summary>
        /// <param name="request">The request to be sent.</param>
        protected virtual void PrepareRequest(HttpRequestMessage request) { }


        public async Task<VerificationResponse> VerifyAsync(PaymentPayloadHeader paymentPayload, PaymentRequirements req, CancellationToken cancellationToken = default)
        {
            logger.LogInformation("Verifying payment payload for resource {Resource} with scheme {Scheme} and asset {Asset}", req.Resource, req.Scheme, req.Asset);
            var body = new FacilitatorRequest
            {
                X402Version = 1,
                PaymentPayload = paymentPayload,
                PaymentRequirements = req
            };

            var url = BuildUrl("/verify", HttpMethod.Post);
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body, options: JsonOptions)
            };
            PrepareRequest(request);
            httpClient.Timeout = TimeSpan.FromSeconds(req.MaxTimeoutSeconds);

            var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                logger.LogWarning("Verification request failed with status {StatusCode}: {Content}", (int)response.StatusCode, content);
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {content}");
            }

            var result = await response.Content.ReadFromJsonAsync<VerificationResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                logger.LogError("Failed to deserialize verification response for resource {Resource}", req.Resource);
                throw new InvalidOperationException("Failed to deserialize verification response");
            }
            logger.LogInformation("Verification result for resource {Resource}: IsValid={IsValid} Reason={Reason}", req.Resource, result.IsValid, result.InvalidReason);
            return result;
        }

        public async Task<SettlementResponse> SettleAsync(PaymentPayloadHeader paymentPayload, PaymentRequirements req, CancellationToken cancellationToken = default)
        {
            logger.LogInformation("Settling payment for resource {Resource} on network {Network} to {PayTo}", req.Resource, req.Network, req.PayTo);
            var body = new FacilitatorRequest
            {
                X402Version = 1,
                PaymentPayload = paymentPayload,
                PaymentRequirements = req
            };

            var url = BuildUrl("/settle", HttpMethod.Post);
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body, options: JsonOptions)
            };
            PrepareRequest(request);
            httpClient.Timeout = TimeSpan.FromSeconds(req.MaxTimeoutSeconds);

            var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                logger.LogWarning("Settlement request failed with status {StatusCode}: {Content}", (int)response.StatusCode, content);
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {content}");
            }

            var result = await response.Content.ReadFromJsonAsync<SettlementResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                logger.LogError("Failed to deserialize settlement response for resource {Resource}", req.Resource);
                throw new InvalidOperationException("Failed to deserialize settlement response");
            }
            logger.LogInformation("Settlement result for resource {Resource}: Success={Success} Tx={Tx}", req.Resource, result.Success, result.Transaction);
            return result;

        }

        public async Task<List<FacilitatorKind>> SupportedAsync(CancellationToken cancellationToken = default)
        {
            logger.LogDebug("Requesting supported facilitator kinds");
            var url = BuildUrl("/supported", HttpMethod.Get);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            PrepareRequest(request);
            httpClient.Timeout = TimeSpan.FromSeconds(20);

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                logger.LogWarning("Supported kinds request failed with status {StatusCode}: {Content}", (int)response.StatusCode, content);
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {content}");
            }

            var map = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(JsonOptions, cancellationToken).ConfigureAwait(false);

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

        public async Task<DiscoveryResponse> DiscoveryAsync(string? type = null, int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
        {
            logger.LogDebug("Requesting discovery resource list");

            var baseUrl = "/discovery/resources";
            var queryParams = new List<string>();

            if (!string.IsNullOrEmpty(type))
                queryParams.Add($"type={Uri.EscapeDataString(type)}");

            if (limit.HasValue)
                queryParams.Add($"limit={limit.Value}");

            if (offset.HasValue)
                queryParams.Add($"offset={offset.Value}");

            var discoveryUrl = queryParams.Count > 0
                ? $"{baseUrl}?{string.Join("&", queryParams)}"
                : baseUrl;

            var url = BuildUrl(discoveryUrl, HttpMethod.Get);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            PrepareRequest(request);
            httpClient.Timeout = TimeSpan.FromSeconds(20);

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                logger.LogWarning("Discover resources request failed with status {StatusCode}: {Content}", (int)response.StatusCode, content);
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {content}");
            }

            var result = await response.Content.ReadFromJsonAsync<DiscoveryResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
            
            return result ?? new();
        }
    }
}
