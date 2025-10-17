using x402.Core.Models;
using x402.Core.Models.Facilitator;

namespace x402.Facilitator
{
    /// <summary>
    /// Client for interacting with the x402 Facilitator service.
    /// Handles payment verification and settlement.
    /// </summary>
    public interface IFacilitatorClient
    {

        /// <summary>
        /// Verifies a payment header against the requirements.
        /// </summary>
        /// <param name="paymentPayload">The X-PAYMENT header value transformed into a payload.</param>
        /// <param name="requirements">The payment requirements.</param>
        /// <returns>Verification response.</returns>
        Task<VerificationResponse> VerifyAsync(PaymentPayloadHeader paymentPayload, PaymentRequirements requirements);

        /// <summary>
        /// Settles a verified payment.
        /// </summary>
        /// <param name="paymentPayload">The X-PAYMENT header value transformed into a payload.</param>
        /// <param name="requirements">The payment requirements.</param>
        /// <returns>Settlement response.</returns>
        Task<SettlementResponse> SettleAsync(PaymentPayloadHeader paymentPayload, PaymentRequirements requirements);

        /// <summary>
        /// Retrieves the set of payment kinds supported by this facilitator.
        /// </summary>
        /// <returns></returns>
        Task<List<FacilitatorKind>> SupportedAsync();

    }
}
