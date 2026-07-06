using System.Numerics;
using x402.Core.Models.v2;

namespace x402.Channels
{
    /// <summary>
    /// A signed off-chain voucher recorded for a single request on a payment channel.
    /// </summary>
    public class ChannelVoucher
    {
        public required PaymentPayloadHeader Payload { get; init; }
        public required PaymentRequirements Requirements { get; init; }

        /// <summary>
        /// The amount charged for this request, in atomic units.
        /// </summary>
        public BigInteger Amount { get; init; }

        public DateTimeOffset Timestamp { get; init; }
    }

    /// <summary>
    /// The state of a payment channel, keyed by network, asset and payer.
    /// </summary>
    public class ChannelState
    {
        public required string ChannelId { get; init; }
        public required string Payer { get; init; }
        public required string Network { get; init; }
        public required string Asset { get; init; }

        /// <summary>
        /// The channel deposit: the authorized maximum from the most recent voucher, in atomic units.
        /// </summary>
        public BigInteger AuthorizedAmount { get; internal set; }

        /// <summary>
        /// Total amount charged on this channel so far (pending + claimed), in atomic units.
        /// </summary>
        public BigInteger TotalCharged { get; internal set; }

        /// <summary>
        /// Amount recorded in vouchers that have not been claimed yet, in atomic units.
        /// </summary>
        public BigInteger PendingAmount { get; internal set; }

        /// <summary>
        /// Amount claimed but not settled yet, in atomic units.
        /// </summary>
        public BigInteger ClaimedAmount { get; internal set; }

        /// <summary>
        /// The unused channel balance (deposit minus total charged) that can be refunded to the client.
        /// </summary>
        public BigInteger Balance => AuthorizedAmount - TotalCharged;

        /// <summary>
        /// Number of vouchers waiting to be claimed.
        /// </summary>
        public int PendingVoucherCount { get; internal set; }

        /// <summary>
        /// Timestamp of the most recent request on this channel.
        /// </summary>
        public DateTimeOffset LastRequestTimestamp { get; internal set; }
    }

    /// <summary>
    /// Context passed to the refund channel selector.
    /// </summary>
    public class RefundContext
    {
        public DateTimeOffset Now { get; init; }
    }

    /// <summary>
    /// Result of a claim cycle batch.
    /// </summary>
    public record ClaimResult(string ChannelId, int Vouchers, string? Transaction);

    /// <summary>
    /// Result of settling a channel's claimed balance.
    /// </summary>
    public record SettleResult(string ChannelId, string Amount, string? Transaction);

    /// <summary>
    /// Result of refunding a channel's unused balance.
    /// </summary>
    public record RefundResult(string Channel, string RefundedAmount);

    /// <summary>
    /// Options controlling the background claim, settle and refund cycles of a <see cref="ChannelManager"/>.
    /// </summary>
    public class ChannelManagerOptions
    {
        /// <summary>
        /// Interval between claim cycles, batching pending vouchers into claims.
        /// </summary>
        public int ClaimIntervalSecs { get; set; } = 60;

        /// <summary>
        /// Interval between settle cycles, settling claimed balances to the merchant wallet.
        /// </summary>
        public int SettleIntervalSecs { get; set; } = 120;

        /// <summary>
        /// Interval between refund cycles, refunding unused balances of idle channels.
        /// </summary>
        public int RefundIntervalSecs { get; set; } = 180;

        /// <summary>
        /// Maximum number of vouchers claimed per channel per claim cycle.
        /// </summary>
        public int MaxClaimsPerBatch { get; set; } = 100;

        /// <summary>
        /// Selects which channels to refund during a refund cycle. When null, no channels are refunded automatically.
        /// </summary>
        public Func<IReadOnlyList<ChannelState>, RefundContext, IEnumerable<ChannelState>>? SelectRefundChannels { get; set; }

        public Action<ClaimResult>? OnClaim { get; set; }
        public Action<SettleResult>? OnSettle { get; set; }
        public Action<RefundResult>? OnRefund { get; set; }
        public Action<Exception>? OnError { get; set; }
    }
}
