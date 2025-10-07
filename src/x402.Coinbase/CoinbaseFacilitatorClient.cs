using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using x402.Coinbase.Models;
using x402.Facilitator;

namespace x402.Coinbase
{
    /// <summary>
    /// https://docs.cdp.coinbase.com/api-reference/v2/rest-api/x402-facilitator/x402-facilitator
    /// </summary>
    public class CoinbaseFacilitatorClient : HttpFacilitatorClient
    {
        private readonly CoinbaseOptions coinbaseOptions;
        private readonly HttpClient httpClient;

        /// <summary>
        /// Creates a new HTTP facilitator client.
        /// </summary>
        /// <param name="baseUrl">The base URL of the facilitator service (trailing slash will be removed)</param>
        public CoinbaseFacilitatorClient(HttpClient httpClient, IOptions<CoinbaseOptions> coinbaseOptions)
            : base(httpClient, Microsoft.Extensions.Logging.Abstractions.NullLogger<HttpFacilitatorClient>.Instance)
        {
            this.httpClient = httpClient;
            this.coinbaseOptions = coinbaseOptions.Value;

            //Remove trailing slash
            this.coinbaseOptions.BaseUrl = this.coinbaseOptions.BaseUrl.EndsWith("/") ? this.coinbaseOptions.BaseUrl[..^1] : this.coinbaseOptions.BaseUrl;
        }


        protected override string BuildUrl(string relativePath, HttpMethod method)
        {
            return $"{coinbaseOptions.BaseUrl}{relativePath}";
        }

        protected override void PrepareRequest(System.Net.Http.HttpRequestMessage request)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            var method = request.Method.Method;
            string accessToken = JWTHelper.GenerateBearerJWT(coinbaseOptions.ApiKeyId, coinbaseOptions.ApiKeySecret, method, url);
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
    }
}
