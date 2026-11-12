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

        /// <summary>
        /// Maximum number of refund attempts that may fail for a single channel before automatic refunds are
        /// abandoned for it. Both a throwing callback and an <see cref="OnRefundAsync"/> returning <c>false</c>
        /// count as failed attempts. When the limit is reached the channel stays tracked (so it can be refunded
        /// manually) and <see cref="OnError"/> is raised, but no further refund callback runs until a new voucher
        /// is recorded on the channel. Zero or negative retries indefinitely. Defaults to <c>0</c> (no limit).
        /// </summary>
        public int MaxRefundAttempts { get; set; }

        /// <summary>
        /// When true, channels selected for refund are removed from tracking without invoking a refund callback
        /// when neither <see cref="OnRefund"/> nor <see cref="OnRefundAsync"/> is configured. Their unused balance
        /// is then never refunded. When false (default), such channels stay tracked so a host that adds a callback
        /// later can still refund them.
        /// </summary>
        public bool RemoveIdleChannelsWithoutRefundCallback { get; set; }

        public Action<ClaimResult>? OnClaim { get; set; }
        public Action<SettleResult>? OnSettle { get; set; }

        /// <summary>
        /// Executes the cooperative refund for a channel selected by <see cref="SelectRefundChannels"/>.
        /// The channel is only removed from tracking when the callback returns without throwing; when it
        /// throws the channel stays tracked and a later refund cycle retries it, up to
        /// <see cref="MaxRefundAttempts"/> attempts.
        /// </summary>
        /// <remarks>
        /// A retry re-raises the same <see cref="RefundResult"/> (same channel and amount), which also happens
        /// when a previous attempt completed on-chain but the callback threw afterwards (for example on a
        /// timeout). Implementations must therefore be idempotent for a given channel and amount: re-executing
        /// the refund must not move funds twice. Use <see cref="OnRefundAsync"/> when the refund performs I/O.
        /// </remarks>
        public Action<RefundResult>? OnRefund { get; set; }

        /// <summary>
        /// Asynchronous variant of <see cref="OnRefund"/>, taking precedence when set. Return <c>true</c>
        /// when the refund completed so the channel can be removed from tracking; return <c>false</c> or
        /// throw to keep the channel tracked for a later retry.
        /// </summary>
        /// <remarks>
        /// Same idempotency requirement as <see cref="OnRefund"/>: a retry re-raises the same
        /// <see cref="RefundResult"/>, so re-running the refund must not move funds twice. A pending refund
        /// (for example one waiting on a client co-signature) should return <c>false</c> to be retried in the
        /// next cycle; raise the retry budget with <see cref="MaxRefundAttempts"/> when a refund can stay
        /// pending for a long time.
        /// </remarks>
        public Func<RefundResult, CancellationToken, Task<bool>>? OnRefundAsync { get; set; }

        public Action<Exception>? OnError { get; set; }
    }
}
