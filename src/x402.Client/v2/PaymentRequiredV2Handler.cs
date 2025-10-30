using System.Text;
using System.Text.Json;
using x402.Client.Events;
using x402.Client.v2.Events;
using x402.Core.Models.v2;

namespace x402.Client.v2
{
    public class PaymentRequiredV2Handler : DelegatingHandler
    {
        private readonly IX402WalletV2 _wallet;
        private readonly int _maxRetries;

        public const string PaymentRequiredHeader = "PAYMENT-REQUIRED";

        public event PaymentRequiredEventHandler? PaymentRequiredReceived;
        public event EventHandler<PaymentSelectedEventArgs>? PaymentSelected;
        public event EventHandler<PaymentRetryEventArgs>? PaymentRetrying;

        public PaymentRequiredV2Handler(IX402WalletV2 wallet, int maxRetries = 1)
            : this(wallet, new HttpClientHandler(), maxRetries) { }

        public PaymentRequiredV2Handler(IX402WalletV2 wallet, HttpMessageHandler innerHandler, int maxRetries = 1)
            : base(innerHandler)
        {
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _maxRetries = maxRetries;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            var retries = 0;
            var response = await base.SendAsync(request, cancellationToken);

            while (response.StatusCode == System.Net.HttpStatusCode.PaymentRequired &&
                   retries < _maxRetries)
            {
                var paymentRequiredResponse = await ParsePaymentRequiredResponseAsync(response);
                if (paymentRequiredResponse == null)
                    break;

                // Notify subscribers
                var canContinue = OnPaymentRequiredReceived(new PaymentRequiredEventArgs(request, response, paymentRequiredResponse));

                if (!canContinue || paymentRequiredResponse.Accepts.Count == 0)
                    break;

                var payment = _wallet.RequestPayment(paymentRequiredResponse, cancellationToken);

                // Notify subscribers
                OnPaymentSelected(new PaymentSelectedEventArgs(request, payment.Requirement, payment.Header, retries + 1));

                if (payment.Requirement == null || payment.Header == null)
                    break;

                var retryRequest = await CloneHttpRequestAsync(request);
                retryRequest.AddPaymentHeader(payment.Header, paymentRequiredResponse.X402Version);

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

        private static async Task<PaymentRequiredResponse?> ParsePaymentRequiredResponseAsync(HttpResponseMessage response)
        {
            try
            {
                // Version 2: Try to parse from PAYMENT-REQUIRED header
                if (response.Headers.TryGetValues(PaymentRequiredHeader, out var headerValues))
                {
                    var headerValue = headerValues.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(headerValue))
                    {
                        try
                        {
                            var decodedBytes = Convert.FromBase64String(headerValue);
                            var jsonString = Encoding.UTF8.GetString(decodedBytes);
                            var parsed = JsonSerializer.Deserialize<PaymentRequiredResponse>(jsonString,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (parsed != null && parsed.X402Version == 2)
                            {
                                return parsed;
                            }
                        }
                        catch
                        {
                            // Fall through to try body parsing
                        }
                    }
                }

            }
            catch
            {
            }

            return null;
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
