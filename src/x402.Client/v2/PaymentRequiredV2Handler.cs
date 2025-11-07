using System.Text;
using System.Text.Json;
using x402.Client.Events;
using x402.Core.Models.v2;

namespace x402.Client.v2
{
    public class PaymentRequiredV2Handler : DelegatingHandler
    {
        private readonly IWalletProvider _walletProvider;
        private readonly int _maxRetries;

        public const string PaymentRequiredHeader = "PAYMENT-REQUIRED";

        public PaymentRequiredV2Handler(IWalletProvider walletProvider, int maxRetries = 1)
        {
            _walletProvider = walletProvider ?? throw new ArgumentNullException(nameof(walletProvider));
            _maxRetries = maxRetries;
        }

        public static PaymentRequiredV2Handler Create(IWalletProvider walletProvider, HttpMessageHandler innerHandler, int maxRetries = 1) => new(walletProvider, innerHandler, maxRetries);

        // Manual chaining constructor, private so it's not called by WebAssembly
        private PaymentRequiredV2Handler(IWalletProvider walletProvider, HttpMessageHandler innerHandler, int maxRetries = 1)
            : base(innerHandler)
        {
            _walletProvider = walletProvider ?? throw new ArgumentNullException(nameof(walletProvider));
            _maxRetries = maxRetries;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            var retries = 0;
            var response = await base.SendAsync(request, cancellationToken);

            if (_walletProvider.Wallet == null)
                return response;

            while (response.StatusCode == System.Net.HttpStatusCode.PaymentRequired &&
                   retries < _maxRetries)
            {
                // Get payment required details from response body
                var paymentRequiredResponse = await ParsePaymentRequiredResponseAsync(response);
                if (paymentRequiredResponse == null)
                    break;

                // Notify subscribers
                var canContinue = _walletProvider.RaisePrepareWallet(new PrepareWalletEventArgs<PaymentRequiredResponse>(request, response, paymentRequiredResponse));

                if (!canContinue || paymentRequiredResponse.Accepts.Count == 0)
                    break;

                var selectedRequirement = await _walletProvider.Wallet.SelectPaymentAsync(paymentRequiredResponse, cancellationToken);

                // Notify subscribers
                _walletProvider.RaiseOnPaymentSelected(new PaymentSelectedEventArgs<PaymentRequirements>(request, selectedRequirement));

                if (selectedRequirement == null)
                    break;

                var header = await _walletProvider.Wallet.CreateHeaderAsync(selectedRequirement, cancellationToken);
                retries++;
                _walletProvider.RaiseOnHeaderCreated(new HeaderCreatedEventArgs<PaymentPayloadHeader>(header, retries));

                var retryRequest = await CloneHttpRequestAsync(request);
                retryRequest.AddPaymentHeader(header, paymentRequiredResponse.X402Version);

                response.Dispose();



                response = await base.SendAsync(retryRequest, cancellationToken);
            }

            return response;
        }

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
