using x402.Core.Models.Facilitator;
using x402.Core.Models.v1;
using x402.Facilitator;

namespace x402.Tests
{
    internal sealed class FakeFacilitatorClient : IFacilitatorClient
    {
        public Func<PaymentPayloadHeader, PaymentRequirements, Task<VerificationResponse>>? VerifyAsyncImpl { get; set; }
        public Func<PaymentPayloadHeader, PaymentRequirements, Task<SettlementResponse>>? SettleAsyncImpl { get; set; }

        public Task<VerificationResponse> VerifyAsync(PaymentPayloadHeader paymentPayload, PaymentRequirements requirements, CancellationToken cancellationToken = default)
        {
            if (VerifyAsyncImpl != null) return VerifyAsyncImpl(paymentPayload, requirements);
            return Task.FromResult(new VerificationResponse { IsValid = true });
        }

        public Task<SettlementResponse> SettleAsync(PaymentPayloadHeader paymentPayload, PaymentRequirements requirements, CancellationToken cancellationToken = default)
        {
            if (SettleAsyncImpl != null) return SettleAsyncImpl(paymentPayload, requirements);
            return Task.FromResult(new SettlementResponse { Success = true, Transaction = "0xabc", Network = requirements.Network });
        }

        public Task<SupportedResponse> SupportedAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SupportedResponse());
        }

        public Task<DiscoveryResponse> DiscoveryAsync(string? type = null, int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DiscoveryResponse { Items = new List<DiscoveryItem>() });
        }
    }
}


