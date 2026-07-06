using x402.Core.Models.Facilitator;
using x402.Facilitator;

namespace x402.Tests
{
    internal sealed class FakeFacilitatorClient : IFacilitatorV2Client
    {
        public Func<Core.Models.v2.PaymentPayloadHeader, Core.Models.v2.PaymentRequirements, Task<VerificationResponse>>? VerifyAsyncImpl { get; set; }
        public Func<Core.Models.v2.PaymentPayloadHeader, Core.Models.v2.PaymentRequirements, Task<SettlementResponse>>? SettleAsyncImpl { get; set; }

        /// <summary>
        /// The settlementAmount passed to the most recent SettleAsync call.
        /// </summary>
        public string? LastSettlementAmount { get; private set; }

        /// <summary>
        /// Number of SettleAsync calls received.
        /// </summary>
        public int SettleCallCount { get; private set; }

        public Task<SupportedResponse> SupportedAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SupportedResponse());
        }

        Task<VerificationResponse> IFacilitatorV2Client.VerifyAsync(Core.Models.v2.PaymentPayloadHeader paymentPayload, Core.Models.v2.PaymentRequirements requirements, CancellationToken cancellationToken)
        {
            if (VerifyAsyncImpl != null) return VerifyAsyncImpl(paymentPayload, requirements);
            return Task.FromResult(new VerificationResponse { IsValid = true });
        }

        Task<SettlementResponse> IFacilitatorV2Client.SettleAsync(Core.Models.v2.PaymentPayloadHeader paymentPayload, Core.Models.v2.PaymentRequirements requirements, string? settlementAmount, CancellationToken cancellationToken)
        {
            LastSettlementAmount = settlementAmount;
            SettleCallCount++;
            if (SettleAsyncImpl != null) return SettleAsyncImpl(paymentPayload, requirements);
            return Task.FromResult(new SettlementResponse { Success = true, Transaction = "0xabc", Network = requirements.Network });
        }

        Task<SupportedResponse> IFacilitatorV2Client.SupportedAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new SupportedResponse());
        }

        Task<Core.Models.v2.Facilitator.DiscoveryResponse> IFacilitatorV2Client.DiscoveryAsync(string? type, int? limit, int? offset, CancellationToken cancellationToken)
        {
            return Task.FromResult(new Core.Models.v2.Facilitator.DiscoveryResponse { Items = new List<Core.Models.v2.Facilitator.DiscoveryItem>() });
        }

        Task<Core.Models.v2.Facilitator.MerchantDiscoveryResponse> IFacilitatorV2Client.DiscoveryMerchantAsync(string payTo, int? limit, int? offset, CancellationToken cancellationToken)
        {
            return Task.FromResult(new Core.Models.v2.Facilitator.MerchantDiscoveryResponse { PayTo = payTo });
        }

        Task<Core.Models.v2.Facilitator.DiscoverySearchResponse> IFacilitatorV2Client.DiscoverySearchAsync(Core.Models.v2.Facilitator.DiscoverySearchRequest searchRequest, CancellationToken cancellationToken)
        {
            return Task.FromResult(new Core.Models.v2.Facilitator.DiscoverySearchResponse());
        }
    }
}


