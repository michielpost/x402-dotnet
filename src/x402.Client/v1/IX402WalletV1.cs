using x402.Client.Models;
using x402.Core.Models.v1;

namespace x402.Client.v1
{
    public interface IX402WalletV1
    {
        List<AssetAllowance> AssetAllowances { get; set; }
        bool IgnoreAllowances { get; set; }

        /// <summary>
        /// Given a list of payment requirements, returns one that can be fulfilled,
        /// and a corresponding payload header to include in the retry.
        /// </summary>
        Task<PaymentRequirements?> SelectPaymentAsync(PaymentRequiredResponse paymentRequiredResponse, CancellationToken cancellationToken = default);

        Task<PaymentPayloadHeader> CreateHeaderAsync(PaymentRequirements requirement, CancellationToken cancellationToken = default);

    }
}
