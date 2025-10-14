using System.Text.Json;
using x402.Models.Responses;

namespace x402.Client
{
    public class PaymentRequiredHandler : DelegatingHandler
    {
        private readonly IX402Wallet _wallet;
        private readonly int _maxRetries;
        private int _globalPaymentsUsed;

        public PaymentRequiredHandler(IX402Wallet wallet, int maxRetries = 1)
        {
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _maxRetries = maxRetries;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var retries = 0;
            var paymentsUsedForThisRequest = 0;

            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

            while (response.StatusCode == System.Net.HttpStatusCode.PaymentRequired &&
                   retries < _maxRetries)
            {
                // Parse the 402 into structured form
                var parsed = await ParsePaymentRequiredResponseAsync(response);

                if (parsed.Accepts.Count == 0)
                    break; // nothing we can fulfill

                // Ask wallet for payment
                var payment = await _wallet.RequestPaymentAsync(parsed.Accepts, cancellationToken);

                if (payment.Requirement == null || payment.Header == null)
                    break; // wallet can't fulfill any

                paymentsUsedForThisRequest++;
                _globalPaymentsUsed++;

                var retryRequest = await CloneHttpRequestAsync(request);

                var headerJson = JsonSerializer.Serialize(payment.Header);
                var base64header = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(headerJson));
                retryRequest.Headers.Add("X-PAYMENT", base64header);

                response.Dispose(); // clean up old response
                response = await base.SendAsync(retryRequest, cancellationToken);

                retries++;
            }

            return response;
        }

        private static async Task<PaymentRequiredResponse> ParsePaymentRequiredResponseAsync(HttpResponseMessage response)
        {
            try
            {
                var content = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(content))
                    return new PaymentRequiredResponse();

                var parsed = JsonSerializer.Deserialize<PaymentRequiredResponse>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return parsed ?? new PaymentRequiredResponse();
            }
            catch
            {
                return new PaymentRequiredResponse();
            }
        }

        private static async Task<HttpRequestMessage> CloneHttpRequestAsync(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = request.Version,
                Content = request.Content == null ? null : new StreamContent(await request.Content.ReadAsStreamAsync())
            };

            foreach (var header in request.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

            if (request.Content != null)
            {
                foreach (var header in request.Content.Headers)
                    clone.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }
}