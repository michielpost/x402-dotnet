using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Numerics;
using x402.Core.Models.v2;
using x402.Facilitator;

namespace x402.Channels
{
    /// <summary>
    /// Manages payment channels for the "batch-settlement" scheme.
    /// Requests are recorded as off-chain vouchers; background cycles periodically batch
    /// voucher claims into a single facilitator settlement per channel, settle claimed
    /// balances, and refund idle channels — so on-chain costs amortize across a session.
    /// </summary>
    public class ChannelManager : IAsyncDisposable
    {
        /// <summary>
        /// Which background cycle currently owns a channel. Guarded by <see cref="Channel.SyncRoot"/>.
        /// </summary>
        private enum ChannelPhase
        {
            /// <summary>No cycle is operating on the channel.</summary>
            Idle,

            /// <summary>A batch of vouchers is being settled with the facilitator.</summary>
            Claiming,

            /// <summary>The claimed balance is being handed to the settle callback.</summary>
            Settling,

            /// <summary>A cooperative refund is in flight.</summary>
            Refunding,
        }

        private sealed class Channel
        {
            public required ChannelState State { get; init; }
            public readonly object SyncRoot = new();
            public readonly Queue<ChannelVoucher> PendingVouchers = new();
            public PaymentPayloadHeader? LatestPayload;
            public PaymentRequirements? LatestRequirements;
            public ChannelPhase Phase = ChannelPhase.Idle;

            /// <summary>Refund attempts that did not complete. Reset whenever a new voucher is recorded.</summary>
            public int RefundAttempts;
        }

        private readonly ILogger<ChannelManager> logger;
        private readonly IFacilitatorV2Client facilitator;
        private readonly ConcurrentDictionary<string, Channel> channels = new();

        /// <summary>
        /// Upper bound for the record/refund revalidation loop in <see cref="RecordVoucher"/>. Only a channel
        /// that is repeatedly refunded (or otherwise replaced) can exhaust it.
        /// </summary>
        private const int MaxChannelLookupAttempts = 64;

        private ChannelManagerOptions options = new();
        private CancellationTokenSource? cts;
        private readonly List<Task> cycleTasks = new();
        private int claimCycleRunning;
        private int settleCycleRunning;
        private int refundCycleRunning;

        public ChannelManager(ILogger<ChannelManager> logger, IFacilitatorV2Client facilitator)
        {
            this.logger = logger;
            this.facilitator = facilitator;
        }

        public bool IsRunning => cts != null;

        /// <summary>
        /// Snapshot of all tracked channels.
        /// </summary>
        public IReadOnlyList<ChannelState> Channels => channels.Values
            .Select(c =>
            {
                lock (c.SyncRoot)
                {
                    return c.State;
                }
            })
            .ToList();

        /// <summary>
        /// Records an off-chain voucher for a verified batch-settlement request.
        /// No on-chain transaction occurs; the amount is claimed in a later claim cycle.
        /// </summary>
        /// <param name="payload">The verified payment payload (voucher).</param>
        /// <param name="requirements">The matched payment requirements.</param>
        /// <param name="amount">The amount charged for this request, in atomic units.</param>
        /// <returns>The channel the voucher was recorded on.</returns>
        public ChannelState RecordVoucher(PaymentPayloadHeader payload, PaymentRequirements requirements, BigInteger amount)
        {
            if (amount < BigInteger.Zero)
                throw new ArgumentOutOfRangeException(nameof(amount), "Voucher amount cannot be negative");

            var payer = payload.ExtractPayerFromPayload() ?? string.Empty;
            var channelId = $"{requirements.Network}|{requirements.Asset}|{payer}".ToLowerInvariant();

            var now = DateTimeOffset.UtcNow;
            Channel channel;
            for (int attempt = 0; ; attempt++)
            {
                if (attempt >= MaxChannelLookupAttempts)
                    throw new InvalidOperationException($"Could not record a voucher on channel {channelId}; the channel keeps being removed or replaced");

                if (channels.TryGetValue(channelId, out var tracked))
                {
                    lock (tracked.SyncRoot)
                    {
                        if (!channels.TryGetValue(channelId, out var current) || !ReferenceEquals(current, tracked))
                            continue; // the channel was removed or replaced between the lookup and the lock

                        if (tracked.Phase == ChannelPhase.Refunding)
                        {
                            // The refund has already been decided for this generation, so the channel is about to be
                            // released on-chain: the voucher starts a new generation instead. It is recorded before
                            // that generation is published, so no cycle can ever observe a channel without a voucher.
                            var replacement = CreateChannel(channelId, payer, requirements);
                            RecordVoucherLocked(replacement, payload, requirements, amount, now);

                            if (channels.TryUpdate(channelId, replacement, tracked))
                            {
                                channel = replacement;
                                break;
                            }

                            continue; // the refund removed the channel first; retry against the current entry
                        }

                        RecordVoucherLocked(tracked, payload, requirements, amount, now);
                        channel = tracked;
                        break;
                    }
                }

                // Not tracked: build the generation first, so the channel is never published without its voucher.
                var fresh = CreateChannel(channelId, payer, requirements);
                RecordVoucherLocked(fresh, payload, requirements, amount, now);

                if (channels.TryAdd(channelId, fresh))
                {
                    channel = fresh;
                    break;
                }
            }

            logger.LogDebug("Recorded voucher of {Amount} on channel {Channel}", amount, channelId);
            return channel.State;
        }

        /// <summary>
        /// Records a voucher on <paramref name="channel"/>. The caller must hold <see cref="Channel.SyncRoot"/>,
        /// or own the channel exclusively because it has not been published yet.
        /// </summary>
        private static void RecordVoucherLocked(
            Channel channel,
            PaymentPayloadHeader payload,
            PaymentRequirements requirements,
            BigInteger amount,
            DateTimeOffset now)
        {
            if (BigInteger.TryParse(payload.Payload.Authorization.Value, out var authorized))
            {
                channel.State.AuthorizedAmount = authorized;
            }

            channel.PendingVouchers.Enqueue(new ChannelVoucher
            {
                Payload = payload,
                Requirements = requirements,
                Amount = amount,
                Timestamp = now
            });
            channel.LatestPayload = payload;
            channel.LatestRequirements = requirements;
            channel.State.PendingAmount += amount;
            channel.State.TotalCharged += amount;
            channel.State.PendingVoucherCount = channel.PendingVouchers.Count;
            channel.State.LastRequestTimestamp = now;

            // A voucher means the channel is active again, so automatic refunds get a fresh set of attempts.
            channel.RefundAttempts = 0;
        }

        /// <summary>
        /// Starts the background claim, settle and refund cycles.
        /// </summary>
        public void Start(ChannelManagerOptions options)
        {
            if (cts != null)
                throw new InvalidOperationException("ChannelManager is already running");

            this.options = options ?? throw new ArgumentNullException(nameof(options));
            cts = new CancellationTokenSource();

            cycleTasks.Add(RunCycleAsync(TimeSpan.FromSeconds(options.ClaimIntervalSecs), ClaimPendingAsync, cts.Token));
            cycleTasks.Add(RunCycleAsync(TimeSpan.FromSeconds(options.SettleIntervalSecs), SettleClaimedAsync, cts.Token));
            cycleTasks.Add(RunCycleAsync(TimeSpan.FromSeconds(options.RefundIntervalSecs), RefundIdleChannelsAsync, cts.Token));

            logger.LogInformation("ChannelManager started (claim={Claim}s settle={Settle}s refund={Refund}s)",
                options.ClaimIntervalSecs, options.SettleIntervalSecs, options.RefundIntervalSecs);
        }

        /// <summary>
        /// Stops the background cycles. When <paramref name="flush"/> is true, runs a final
        /// claim and settle cycle so no recorded vouchers are left unclaimed.
        /// </summary>
        public async Task StopAsync(bool flush = false)
        {
            var currentCts = cts;
            if (currentCts != null)
            {
                cts = null;
                currentCts.Cancel();
                try
                {
                    await Task.WhenAll(cycleTasks).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                cycleTasks.Clear();
                currentCts.Dispose();
            }

            if (flush)
            {
                await ClaimPendingAsync(CancellationToken.None).ConfigureAwait(false);
                await SettleClaimedAsync(CancellationToken.None).ConfigureAwait(false);
            }

            logger.LogInformation("ChannelManager stopped (flush={Flush})", flush);
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync(flush: false).ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        private async Task RunCycleAsync(TimeSpan interval, Func<CancellationToken, Task> cycle, CancellationToken token)
        {
            if (interval <= TimeSpan.Zero)
                return;

            using var timer = new PeriodicTimer(interval);
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    try
                    {
                        await cycle(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ReportError(ex);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>
        /// Runs a single claim cycle: batches pending vouchers per channel (up to
        /// <see cref="ChannelManagerOptions.MaxClaimsPerBatch"/>) into one facilitator settlement.
        /// </summary>
        public async Task ClaimPendingAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref claimCycleRunning, 1) == 1)
                return;

            try
            {
                foreach (var channel in channels.Values)
                {
                    await ClaimChannelAsync(channel, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                Interlocked.Exchange(ref claimCycleRunning, 0);
            }
        }

        /// <summary>
        /// Claims the vouchers of a single channel. The batch is only peeked from the queue: it keeps
        /// counting towards <see cref="ChannelState.PendingAmount"/> until the facilitator accepts it, so
        /// a concurrent refund cycle can never observe a channel that looks fully refundable mid-claim.
        /// A failed claim therefore needs no requeue and keeps the vouchers in their original order.
        /// </summary>
        private async Task ClaimChannelAsync(Channel channel, CancellationToken cancellationToken)
        {
            List<ChannelVoucher> batch;
            BigInteger batchAmount;
            PaymentPayloadHeader payload;
            PaymentRequirements requirements;

            lock (channel.SyncRoot)
            {
                if (channel.Phase != ChannelPhase.Idle || channel.PendingVouchers.Count == 0)
                    return;

                int take = Math.Min(channel.PendingVouchers.Count, Math.Max(1, options.MaxClaimsPerBatch));
                batch = channel.PendingVouchers.Take(take).ToList();
                batchAmount = batch.Aggregate(BigInteger.Zero, static (sum, voucher) => sum + voucher.Amount);

                // The most recent voucher in the batch supersedes the earlier ones on-chain.
                payload = batch[^1].Payload;
                requirements = batch[^1].Requirements;
                channel.Phase = ChannelPhase.Claiming;
            }

            try
            {
                if (batchAmount == BigInteger.Zero)
                {
                    // Nothing to move on-chain; the vouchers are simply consumed.
                    CompleteClaim(channel, batch.Count, BigInteger.Zero, null);
                    return;
                }

                var response = await facilitator.SettleAsync(payload, requirements, batchAmount.ToString(), cancellationToken).ConfigureAwait(false);
                if (response.Success)
                {
                    CompleteClaim(channel, batch.Count, batchAmount, response.Transaction);
                    return;
                }

                ResetPhase(channel);
                ReportError(new InvalidOperationException($"Claim failed for channel {channel.State.ChannelId}: {response.ErrorReason}"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ResetPhase(channel);
                throw;
            }
            catch (Exception ex)
            {
                ResetPhase(channel);
                ReportError(ex);
            }
        }

        private void CompleteClaim(Channel channel, int voucherCount, BigInteger batchAmount, string? transaction)
        {
            lock (channel.SyncRoot)
            {
                for (int i = 0; i < voucherCount && channel.PendingVouchers.Count > 0; i++)
                {
                    channel.PendingVouchers.Dequeue();
                }

                channel.State.PendingAmount -= batchAmount;
                channel.State.ClaimedAmount += batchAmount;
                channel.State.PendingVoucherCount = channel.PendingVouchers.Count;
                channel.Phase = ChannelPhase.Idle;
            }

            if (batchAmount != BigInteger.Zero)
            {
                logger.LogInformation("Claimed {Count} vouchers ({Amount}) on channel {Channel} (tx: {Tx})",
                    voucherCount, batchAmount, channel.State.ChannelId, transaction);
            }

            InvokeCallback(options.OnClaim, new ClaimResult(channel.State.ChannelId, voucherCount, transaction));
        }

        /// <summary>
        /// Runs a single settle cycle: finalizes claimed balances per channel and raises
        /// <see cref="ChannelManagerOptions.OnSettle"/>. Claims are settled to the merchant
        /// wallet by the facilitator at claim time; this cycle closes out the local bookkeeping.
        /// </summary>
        public Task SettleClaimedAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref settleCycleRunning, 1) == 1)
                return Task.CompletedTask;

            try
            {
                foreach (var channel in channels.Values)
                {
                    SettleChannel(channel);
                }
            }
            finally
            {
                Interlocked.Exchange(ref settleCycleRunning, 0);
            }

            return Task.CompletedTask;
        }

        private void SettleChannel(Channel channel)
        {
            BigInteger settled;
            lock (channel.SyncRoot)
            {
                if (channel.Phase != ChannelPhase.Idle)
                    return;

                settled = channel.State.ClaimedAmount;
                if (settled == BigInteger.Zero)
                    return;

                channel.Phase = ChannelPhase.Settling;
            }

            Exception? failure = null;
            try
            {
                options.OnSettle?.Invoke(new SettleResult(channel.State.ChannelId, settled.ToString(), null));
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            lock (channel.SyncRoot)
            {
                // Only drop the claimed amount once the host has taken ownership of it. A failed callback
                // keeps the balance tracked so the next settle cycle retries instead of losing it.
                if (failure == null)
                {
                    channel.State.ClaimedAmount -= settled;
                }
                channel.Phase = ChannelPhase.Idle;
            }

            if (failure != null)
            {
                ReportError(failure);
                return;
            }

            logger.LogInformation("Settled {Amount} on channel {Channel}", settled, channel.State.ChannelId);
        }

        /// <summary>
        /// Runs a single refund cycle: asks <see cref="ChannelManagerOptions.SelectRefundChannels"/>
        /// which idle channels to release and raises <see cref="ChannelManagerOptions.OnRefund"/> (or
        /// <see cref="ChannelManagerOptions.OnRefundAsync"/>) so the host can execute the cooperative refund.
        /// A channel is only removed from tracking once that callback reports success; a throwing or
        /// unsuccessful callback leaves the channel tracked so a later cycle retries, up to
        /// <see cref="ChannelManagerOptions.MaxRefundAttempts"/> attempts.
        /// Channels with unclaimed, unsettled, in-flight or already refunding amounts are never refunded.
        /// </summary>
        public async Task RefundIdleChannelsAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref refundCycleRunning, 1) == 1)
                return;

            try
            {
                var selector = options.SelectRefundChannels;
                if (selector == null)
                    return;

                if (options.OnRefund == null && options.OnRefundAsync == null)
                {
                    if (!options.RemoveIdleChannelsWithoutRefundCallback)
                    {
                        logger.LogWarning("Refund cycle skipped: no OnRefund or OnRefundAsync callback is configured to execute the refund, so idle channels stay tracked (set RemoveIdleChannelsWithoutRefundCallback to remove them without refunding)");
                        return;
                    }

                    logger.LogWarning("No OnRefund or OnRefundAsync callback is configured; idle channels are removed from tracking without being refunded (RemoveIdleChannelsWithoutRefundCallback is set)");
                }

                var snapshot = channels.Values.Select(c =>
                {
                    lock (c.SyncRoot)
                    {
                        return c.State;
                    }
                }).ToList();

                var selected = selector(snapshot, new RefundContext { Now = DateTimeOffset.UtcNow })?.ToList();
                if (selected == null || selected.Count == 0)
                    return;

                foreach (var state in selected)
                {
                    if (!channels.TryGetValue(state.ChannelId, out var channel))
                        continue;

                    await RefundChannelAsync(channel, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                Interlocked.Exchange(ref refundCycleRunning, 0);
            }
        }

        private async Task RefundChannelAsync(Channel channel, CancellationToken cancellationToken)
        {
            var channelId = channel.State.ChannelId;
            bool hasCallback = options.OnRefund != null || options.OnRefundAsync != null;
            BigInteger balance;

            lock (channel.SyncRoot)
            {
                if (channel.Phase != ChannelPhase.Idle)
                {
                    logger.LogDebug("Skipping refund of channel {Channel}; a {Phase} cycle is in flight", channelId, channel.Phase);
                    return;
                }

                if (channel.PendingVouchers.Count > 0
                    || channel.State.PendingAmount != BigInteger.Zero
                    || channel.State.ClaimedAmount != BigInteger.Zero)
                {
                    logger.LogDebug("Skipping refund of channel {Channel}; it has unclaimed or unsettled amounts", channelId);
                    return;
                }

                if (hasCallback && IsRefundAbandoned(channel))
                {
                    logger.LogDebug("Skipping refund of channel {Channel}; automatic refunds were abandoned after {Attempts} failed attempts", channelId, channel.RefundAttempts);
                    return;
                }

                channel.Phase = ChannelPhase.Refunding;
                balance = channel.State.Balance;
            }

            var result = new RefundResult(channelId, balance.ToString());

            if (hasCallback)
            {
                bool refunded;
                try
                {
                    refunded = await InvokeRefundCallbackAsync(result, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    ResetPhase(channel);
                    throw;
                }
                catch (Exception ex)
                {
                    ResetPhase(channel);
                    ReportError(ex);
                    RegisterRefundFailure(channel, channelId, ex);
                    return;
                }

                if (!refunded)
                {
                    ResetPhase(channel);
                    RegisterRefundFailure(channel, channelId, null);
                    return;
                }
            }
            else
            {
                // Explicitly opted in: the host accepts that unused balances are dropped from tracking.
                logger.LogWarning("Removing idle channel {Channel} without refunding its unused balance {Balance}: no OnRefund or OnRefundAsync callback is configured", channelId, balance);
            }

            // Remove only this exact channel instance: vouchers recorded while the refund was in flight
            // start a replacement generation that is already tracked and must stay claimable.
            if (channels.TryRemove(new KeyValuePair<string, Channel>(channelId, channel)))
            {
                logger.LogInformation("Refunded channel {Channel} (unused balance {Balance})", channelId, balance);
            }
            else
            {
                logger.LogInformation("Refunded channel {Channel} (unused balance {Balance}); a newer channel generation is already tracked", channelId, balance);
            }
        }

        private async Task<bool> InvokeRefundCallbackAsync(RefundResult result, CancellationToken cancellationToken)
        {
            var asyncCallback = options.OnRefundAsync;
            if (asyncCallback != null)
                return await asyncCallback(result, cancellationToken).ConfigureAwait(false);

            var callback = options.OnRefund;
            if (callback != null)
            {
                callback(result);
                return true;
            }

            return false;
        }

        private static Channel CreateChannel(string channelId, string payer, PaymentRequirements requirements) => new()
        {
            State = new ChannelState
            {
                ChannelId = channelId,
                Payer = payer,
                Network = requirements.Network,
                Asset = requirements.Asset,
            }
        };

        private static void ResetPhase(Channel channel)
        {
            lock (channel.SyncRoot)
            {
                channel.Phase = ChannelPhase.Idle;
            }
        }

        /// <summary>
        /// True when automatic refunds for the channel were abandoned after
        /// <see cref="ChannelManagerOptions.MaxRefundAttempts"/> failed attempts. The channel stays tracked so
        /// the host can still refund it; a new voucher resets the attempt count. Caller holds the channel lock.
        /// </summary>
        private bool IsRefundAbandoned(Channel channel)
        {
            int max = options.MaxRefundAttempts;
            return max > 0 && channel.RefundAttempts >= max;
        }

        /// <summary>
        /// Counts a refund attempt that did not complete and reports the channel once the retry budget is spent.
        /// </summary>
        private void RegisterRefundFailure(Channel channel, string channelId, Exception? failure)
        {
            int attempts;
            lock (channel.SyncRoot)
            {
                attempts = ++channel.RefundAttempts;
            }

            if (!IsRefundAbandoned(channel))
            {
                logger.LogWarning(
                    "Refund of channel {Channel} did not complete (attempt {Attempts}); the channel stays tracked so a later cycle can retry",
                    channelId, attempts);
                return;
            }

            ReportError(new InvalidOperationException(
                $"Giving up automatic refund of channel {channelId} after {attempts} failed attempts"
                + (failure == null ? "; the refund callback reported the refund did not complete" : $": {failure.Message}")
                + ". The channel stays tracked so it can be refunded manually; recording a new voucher restarts automatic refunds.",
                failure));
        }

        private void InvokeCallback<T>(Action<T>? callback, T result)
        {
            if (callback == null)
                return;

            try
            {
                callback(result);
            }
            catch (Exception ex)
            {
                ReportError(ex);
            }
        }

        private void ReportError(Exception ex)
        {
            logger.LogError(ex, "ChannelManager cycle error");
            try
            {
                options.OnError?.Invoke(ex);
            }
            catch (Exception callbackEx)
            {
                logger.LogError(callbackEx, "ChannelManager OnError callback threw");
            }
        }
    }
}
