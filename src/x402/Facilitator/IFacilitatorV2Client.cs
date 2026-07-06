using x402.Core.Models.Facilitator;
using x402.Core.Models.v2;
using x402.Core.Models.v2.Facilitator;

namespace x402.Facilitator
{
    /// <summary>
    /// Client for interacting with the x402 Facilitator service.
    /// Handles payment verification and settlement.
    /// </summary>
    public interface IFacilitatorV2Client
    {

        /// <summary>
        /// Verifies a payment header against the requirements.
        /// </summary>
        /// <param name="paymentPayload">The PAYMENT-SIGNATURE header value transformed into a payload.</param>
        /// <param name="requirements">The payment requirements.</param>
        /// <param name="cancellationToken">Optional cancellation token to cancel the operation.</param>
        /// <returns>Verification response.</returns>
        Task<VerificationResponse> VerifyAsync(PaymentPayloadHeader paymentPayload, PaymentRequirements requirements, CancellationToken cancellationToken = default);

        /// <summary>
        /// Settles a verified payment.
        /// </summary>
        /// <param name="paymentPayload">The PAYMENT-SIGNATURE header value transformed into a payload.</param>
        /// <param name="requirements">The payment requirements.</param>
        /// <param name="settlementAmount">Optional actual amount to settle in atomic units, for schemes ("upto", "batch-settlement") that settle less than the authorized maximum. Null settles the full authorized amount.</param>
        /// <param name="cancellationToken">Optional cancellation token to cancel the operation.</param>
        /// <returns>Settlement response.</returns>
        Task<SettlementResponse> SettleAsync(PaymentPayloadHeader paymentPayload, PaymentRequirements requirements, string? settlementAmount = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves the set of payment kinds supported by this facilitator.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token to cancel the operation.</param>
        /// <returns></returns>
        Task<SupportedResponse> SupportedAsync(CancellationToken cancellationToken = default);


        /// <summary>
        /// Lists all active discovered x402 resources (GET discovery/resources).
        /// </summary>
        /// <param name="type">Optional filter by protocol type (e.g., "http", "mcp").</param>
        /// <param name="limit">The number of resources to return per page.</param>
        /// <param name="offset">The offset of the first resource to return.</param>
        /// <param name="cancellationToken">Optional cancellation token to cancel the operation.</param>
        /// <returns>Paginated list of discovered resources.</returns>
        Task<DiscoveryResponse> DiscoveryAsync(string? type = null, int? limit = null, int? offset = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets x402 merchant discovery information for a given merchant payment address
        /// (GET discovery/merchant). Returns all active resources whose payment requirements
        /// route funds to the given payTo address; the list is empty when none are found.
        /// </summary>
        /// <param name="payTo">The merchant's payment address to look up.</param>
        /// <param name="limit">The number of resources to return per page.</param>
        /// <param name="offset">The offset of the first resource to return.</param>
        /// <param name="cancellationToken">Optional cancellation token to cancel the operation.</param>
        /// <returns>Paginated list of the merchant's discovered resources.</returns>
        Task<MerchantDiscoveryResponse> DiscoveryMerchantAsync(string payTo, int? limit = null, int? offset = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Searches for active x402 resources using a text query and optional filters
        /// (GET discovery/search). Results are sorted by relevance and quality score.
        /// </summary>
        /// <param name="searchRequest">The search query and filters.</param>
        /// <param name="cancellationToken">Optional cancellation token to cancel the operation.</param>
        /// <returns>Resources matching the query and filters.</returns>
        Task<DiscoverySearchResponse> DiscoverySearchAsync(DiscoverySearchRequest searchRequest, CancellationToken cancellationToken = default);

    }
}
