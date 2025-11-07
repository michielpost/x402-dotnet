using System.Text.Json;
using x402.Client.Events;
using x402.Core.Models.v1;

namespace x402.Client.v1
{

    public class PaymentRequiredV1Handler : DelegatingHandler
    {
        private readonly IWalletProvider _walletProvider;
        private readonly int _maxRetries;

        public PaymentRequiredV1Handler(IWalletProvider walletProvider, int maxRetries = 1)
        {
            _walletProvider = walletProvider ?? throw new ArgumentNullException(nameof(walletProvider));
            _maxRetries = maxRetries;
        }

        public static PaymentRequiredV1Handler Create(IWalletProvider walletProvider, HttpMessageHandler? innerHandler = null, int maxRetries = 1) => new(walletProvider, innerHandler ?? new HttpClientHandler(), maxRetries);

        // Manual chaining constructor, private so it's not called by WebAssembly
        private PaymentRequiredV1Handler(IWalletProvider walletProvider, HttpMessageHandler innerHandler, int maxRetries = 1)
            : base(innerHandler)
        {
            _walletProvider = walletProvider ?? throw new ArgumentNullException(nameof(walletProvider));
            _maxRetries = maxRetries;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            var retries = 0;
            var response = await base.SendAsync(request, cancellationToken);

            while (response.StatusCode == System.Net.HttpStatusCode.PaymentRequired &&
                   retries < _maxRetries)
            {
                // Get payment required details from response body
                var paymentRequiredResponse = await ParsePaymentRequiredResponseAsync(response);
                if (paymentRequiredResponse == null)
                    break;

                // Notify subscribers 
                var canContinue = await _walletProvider.RaisePrepareWalletAsync(new PrepareWalletEventArgs<PaymentRequiredResponse>(request, response, paymentRequiredResponse));

                if (_walletProvider.Wallet == null)
                    return response;

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
                // Version 1: Parse from JSON body
                var content = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(content))
                    return new PaymentRequiredResponse();

                var bodyParsed = JsonSerializer.Deserialize<PaymentRequiredResponse>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return bodyParsed ?? new PaymentRequiredResponse();
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
