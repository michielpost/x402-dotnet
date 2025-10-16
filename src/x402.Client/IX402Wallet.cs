using x402.Client.Models;
using x402.Core.Models;

namespace x402.Client
{
    public interface IX402Wallet
    {
        List<AssetAllowance> AssetAllowances { get; set; }
        bool IgnoreAllowances { get; set; }

        /// <summary>
        /// Given a list of payment requirements, returns one that can be fulfilled,
        /// and a corresponding payload header to include in the retry.
        /// </summary>
        (PaymentRequirements? Requirement, PaymentPayloadHeader? Header) RequestPayment(IReadOnlyList<PaymentRequirements> requirements, CancellationToken cancellationToken);
    }
}
