using x402.Core.Models.Facilitator;

namespace x402.Core.Models
{
    public record HandleX402Result(
        bool CanContinueRequest,
        PaymentRequirements PaymentRequirements,
        string? Error = null,
        VerificationResponse? VerificationResponse = null,
        SettlementResponse? SettlementResponse = null
        );
}
