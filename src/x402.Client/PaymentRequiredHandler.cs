using System.Text.Json;
using x402.Client.Events;
using x402.Core.Models.v1;

namespace x402.Client
{
    public class PaymentRequiredHandler : DelegatingHandler
    {
        private readonly IX402Wallet _wallet;
        private readonly int _maxRetries;

        protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public event PaymentRequiredEventHandler? PaymentRequiredReceived;
        public event EventHandler<PaymentSelectedEventArgs>? PaymentSelected;
        public event EventHandler<PaymentRetryEventArgs>? PaymentRetrying;

        public PaymentRequiredHandler(IX402Wallet wallet, int maxRetries = 1)
            : this(wallet, maxRetries, new HttpClientHandler()) { }

        public PaymentRequiredHandler(IX402Wallet wallet, int maxRetries, HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _maxRetries = maxRetries;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var retries = 0;
            var response = await base.SendAsync(request, cancellationToken);

            while (response.StatusCode == System.Net.HttpStatusCode.PaymentRequired &&
                   retries < _maxRetries)
            {
                var paymentRequiredResponse = await ParsePaymentRequiredResponseAsync(response);

                // Notify subscribers
                var canContinue = OnPaymentRequiredReceived(new PaymentRequiredEventArgs(request, response, paymentRequiredResponse));

                if (!canContinue || paymentRequiredResponse.Accepts.Count == 0)
                    break;

                var payment = _wallet.RequestPayment(paymentRequiredResponse.Accepts, cancellationToken);

                // Notify subscribers
                OnPaymentSelected(new PaymentSelectedEventArgs(request, payment.Requirement, payment.Header, retries + 1));

                if (payment.Requirement == null || payment.Header == null)
                    break;

                var retryRequest = await CloneHttpRequestAsync(request);
                var headerJson = JsonSerializer.Serialize(payment.Header, JsonOptions);
                var base64header = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(headerJson));
                retryRequest.Headers.Add("X-PAYMENT", base64header);

                response.Dispose();

                retries++;
                OnPaymentRetrying(new PaymentRetryEventArgs(retryRequest, retries));

                response = await base.SendAsync(retryRequest, cancellationToken);
            }

            return response;
        }

        protected virtual bool OnPaymentRequiredReceived(PaymentRequiredEventArgs e)
        {
            var canContinue = true;
            if (PaymentRequiredReceived != null)
            {
                // If any subscriber returns false, we should not continue
                foreach (PaymentRequiredEventHandler handler in PaymentRequiredReceived.GetInvocationList())
                {
                    if (!handler(this, e))
                    {
                        canContinue = false;
                        break;
                    }
                }
            }
            return canContinue;
        }

        protected virtual void OnPaymentSelected(PaymentSelectedEventArgs e)
            => PaymentSelected?.Invoke(this, e);

        protected virtual void OnPaymentRetrying(PaymentRetryEventArgs e)
            => PaymentRetrying?.Invoke(this, e);

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
