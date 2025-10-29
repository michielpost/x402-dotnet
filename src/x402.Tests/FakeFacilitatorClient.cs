using x402.Core.Models.Facilitator;
using x402.Facilitator;

namespace x402.Tests
{
    internal sealed class FakeFacilitatorClient : IFacilitatorV1Client, IFacilitatorV2Client
    {
        public Func<Core.Models.v1.PaymentPayloadHeader, Core.Models.v1.PaymentRequirements, Task<VerificationResponse>>? VerifyAsyncImpl { get; set; }
        public Func<Core.Models.v1.PaymentPayloadHeader, Core.Models.v1.PaymentRequirements, Task<SettlementResponse>>? SettleAsyncImpl { get; set; }

        public Func<Core.Models.v2.PaymentPayloadHeader, Core.Models.v2.PaymentRequirements, Task<VerificationResponse>>? VerifyV2AsyncImpl { get; set; }
        public Func<Core.Models.v2.PaymentPayloadHeader, Core.Models.v2.PaymentRequirements, Task<SettlementResponse>>? SettleV2AsyncImpl { get; set; }


        public Task<VerificationResponse> VerifyAsync(Core.Models.v1.PaymentPayloadHeader paymentPayload, Core.Models.v1.PaymentRequirements requirements, CancellationToken cancellationToken = default)
        {
            if (VerifyAsyncImpl != null) return VerifyAsyncImpl(paymentPayload, requirements);
            return Task.FromResult(new VerificationResponse { IsValid = true });
        }

        public Task<SettlementResponse> SettleAsync(Core.Models.v1.PaymentPayloadHeader paymentPayload, Core.Models.v1.PaymentRequirements requirements, CancellationToken cancellationToken = default)
        {
            if (SettleAsyncImpl != null) return SettleAsyncImpl(paymentPayload, requirements);
            return Task.FromResult(new SettlementResponse { Success = true, Transaction = "0xabc", Network = requirements.Network });
        }

        public Task<SupportedResponse> SupportedAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SupportedResponse());
        }

        public Task<Core.Models.v1.Facilitator.DiscoveryResponse> DiscoveryAsync(string? type = null, int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new Core.Models.v1.Facilitator.DiscoveryResponse { Items = new() });
        }

        Task<VerificationResponse> IFacilitatorV2Client.VerifyAsync(Core.Models.v2.PaymentPayloadHeader paymentPayload, Core.Models.v2.PaymentRequirements requirements, CancellationToken cancellationToken)
        {
            if (VerifyV2AsyncImpl != null) return VerifyV2AsyncImpl(paymentPayload, requirements);
            return Task.FromResult(new VerificationResponse { IsValid = true });
        }

        Task<SettlementResponse> IFacilitatorV2Client.SettleAsync(Core.Models.v2.PaymentPayloadHeader paymentPayload, Core.Models.v2.PaymentRequirements requirements, CancellationToken cancellationToken)
        {
            if (SettleV2AsyncImpl != null) return SettleV2AsyncImpl(paymentPayload, requirements);
            return Task.FromResult(new SettlementResponse { Success = true, Transaction = "0xabc", Network = requirements.Network });
        }

        Task<SupportedResponse> IFacilitatorV2Client.SupportedV2Async(CancellationToken cancellationToken)
        {
            return Task.FromResult(new SupportedResponse());
        }

        Task<Core.Models.v2.Facilitator.DiscoveryResponse> IFacilitatorV2Client.DiscoveryV2Async(string? type, int? limit, int? offset, CancellationToken cancellationToken)
        {
            return Task.FromResult(new Core.Models.v2.Facilitator.DiscoveryResponse { Items = new List<Core.Models.v2.Facilitator.DiscoveryItem>() });
        }
    }
}


