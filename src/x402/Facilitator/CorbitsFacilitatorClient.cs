using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using x402.Facilitator.Models;
using x402.Models;
using x402.Models.Responses;

namespace x402.Facilitator
{
    public class CorbitsFacilitatorClient : HttpFacilitatorClient
    {
        private readonly HttpClient httpClient;
        private readonly ILogger<CorbitsFacilitatorClient> logger;

        public CorbitsFacilitatorClient(HttpClient httpClient, ILogger<CorbitsFacilitatorClient> logger)
            : base(httpClient, logger)
        {
            this.httpClient = httpClient;
            this.logger = logger;
        }

        public override Task<VerificationResponse> VerifyAsync(PaymentPayloadHeader paymentPayload, PaymentRequirements req)
        {
            return Task.FromResult(new VerificationResponse
            {
                IsValid = true
            });
        }

        /// <summary>
        /// Calls the facilitator's /accepts endpoint to get updated payment requirements with facilitator-specific data.
        /// </summary>
        /// <param name="requirements">The original payment requirements</param>
        /// <returns>Updated payment requirements from the facilitator</returns>
        public async Task<List<PaymentRequirements>> AcceptsAsync(List<PaymentRequirements> requirements)
        {
            logger.LogInformation("Calling facilitator /accepts endpoint with {Count} requirements", requirements.Count);

            var requestBody = new PaymentRequiredResponse
            {
                X402Version = 1,
                Accepts = requirements
            };

            var url = BuildUrl("/accepts", HttpMethod.Post);
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(requestBody, options: JsonOptions)
            };
            PrepareRequest(request);

            var response = await httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                logger.LogWarning("Accepts request failed with status {StatusCode}: {Content}", (int)response.StatusCode, content);
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {content}");
            }

            var result = await response.Content.ReadFromJsonAsync<PaymentRequiredResponse>(JsonOptions).ConfigureAwait(false);
            if (result is null)
            {
                logger.LogError("Failed to deserialize accepts response");
                throw new InvalidOperationException("Failed to deserialize accepts response");
            }

            logger.LogInformation("Facilitator returned {Count} updated requirements", result.Accepts.Count);
            return result.Accepts;
        }

        protected override object GetPaymentHeaderForRequest(PaymentPayloadHeader paymentPayload)
        {
            return paymentPayload.RawHeader ?? throw new InvalidOperationException("RawHeader is required for Corbits facilitator");
        }
    }
}
