﻿using System.Text.Json;
using x402.Client.Events;
using x402.Client.v1.Events;
using x402.Core.Models.v1;

namespace x402.Client.v1
{

    public class PaymentRequiredV1Handler : DelegatingHandler
    {
        private readonly IWalletProvider _walletProvider;
        private readonly int _maxRetries;

        public event PaymentRequiredEventHandler? PaymentRequiredReceived;
        public event EventHandler<PaymentSelectedEventArgs>? PaymentSelected;
        public event EventHandler<PaymentRetryEventArgs>? PaymentRetrying;

        public PaymentRequiredV1Handler(IWalletProvider walletProvider, int maxRetries = 1)
            : this(walletProvider, new HttpClientHandler(), maxRetries) { }

        public PaymentRequiredV1Handler(IWalletProvider walletProvider, HttpMessageHandler innerHandler, int maxRetries = 1)
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
                var canContinue = OnPaymentRequiredReceived(new PaymentRequiredEventArgs(request, response, paymentRequiredResponse));


                if (!canContinue || paymentRequiredResponse.Accepts.Count == 0)
                    break;

                var payment = await _walletProvider.Wallet.RequestPaymentAsync(paymentRequiredResponse, cancellationToken);

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
