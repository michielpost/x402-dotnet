using x402.Facilitator.Models;

namespace x402.Models
{
    public record HandleX402Result(bool CanContinueRequest, string? Error = null, VerificationResponse? VerificationResponse = null);
}
