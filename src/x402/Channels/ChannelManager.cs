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
        private sealed class Channel
        {
            public required ChannelState State { get; init; }
            public readonly object SyncRoot = new();
            public readonly Queue<ChannelVoucher> PendingVouchers = new();
            public PaymentPayloadHeader? LatestPayload;
            public PaymentRequirements? LatestRequirements;
        }

        private readonly ILogger<ChannelManager> logger;
        private readonly IFacilitatorV2Client facilitator;
        private readonly ConcurrentDictionary<string, Channel> channels = new();

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
        public IReadOnlyList<ChannelState> Channels => channels.Values.Select(c => c.State).ToList();

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

            var channel = channels.GetOrAdd(channelId, _ => new Channel
            {
                State = new ChannelState
                {
                    ChannelId = channelId,
                    Payer = payer,
                    Network = requirements.Network,
                    Asset = requirements.Asset,
                }
            });

            lock (channel.SyncRoot)
            {
                var now = DateTimeOffset.UtcNow;
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
            }

            logger.LogDebug("Recorded voucher of {Amount} on channel {Channel}", amount, channelId);
            return channel.State;
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
                    List<ChannelVoucher> batch;
                    BigInteger batchAmount = BigInteger.Zero;
                    PaymentPayloadHeader? payload;
                    PaymentRequirements? requirements;

                    lock (channel.SyncRoot)
                    {
                        if (channel.PendingVouchers.Count == 0)
                            continue;

                        int take = Math.Min(channel.PendingVouchers.Count, Math.Max(1, options.MaxClaimsPerBatch));
                        batch = new List<ChannelVoucher>(take);
                        for (int i = 0; i < take; i++)
                        {
                            var voucher = channel.PendingVouchers.Dequeue();
                            batch.Add(voucher);
                            batchAmount += voucher.Amount;
                        }
                        channel.State.PendingAmount -= batchAmount;
                        channel.State.PendingVoucherCount = channel.PendingVouchers.Count;

                        // The most recent voucher in the batch supersedes the earlier ones on-chain.
                        payload = batch[^1].Payload;
                        requirements = batch[^1].Requirements;
                    }

                    if (batchAmount == BigInteger.Zero)
                    {
                        // Nothing to move on-chain; the vouchers are simply consumed.
                        options.OnClaim?.Invoke(new ClaimResult(channel.State.ChannelId, batch.Count, null));
                        continue;
                    }

                    try
                    {
                        var response = await facilitator.SettleAsync(payload!, requirements!, batchAmount.ToString(), cancellationToken).ConfigureAwait(false);
                        if (response.Success)
                        {
                            lock (channel.SyncRoot)
                            {
                                channel.State.ClaimedAmount += batchAmount;
                            }
                            logger.LogInformation("Claimed {Count} vouchers ({Amount}) on channel {Channel} (tx: {Tx})",
                                batch.Count, batchAmount, channel.State.ChannelId, response.Transaction);
                            options.OnClaim?.Invoke(new ClaimResult(channel.State.ChannelId, batch.Count, response.Transaction));
                        }
                        else
                        {
                            Requeue(channel, batch, batchAmount);
                            ReportError(new InvalidOperationException($"Claim failed for channel {channel.State.ChannelId}: {response.ErrorReason}"));
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        Requeue(channel, batch, batchAmount);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Requeue(channel, batch, batchAmount);
                        ReportError(ex);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref claimCycleRunning, 0);
            }
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
                    BigInteger settled;
                    lock (channel.SyncRoot)
                    {
                        settled = channel.State.ClaimedAmount;
                        if (settled == BigInteger.Zero)
                            continue;
                        channel.State.ClaimedAmount = BigInteger.Zero;
                    }

                    logger.LogInformation("Settled {Amount} on channel {Channel}", settled, channel.State.ChannelId);
                    try
                    {
                        options.OnSettle?.Invoke(new SettleResult(channel.State.ChannelId, settled.ToString(), null));
                    }
                    catch (Exception ex)
                    {
                        ReportError(ex);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref settleCycleRunning, 0);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Runs a single refund cycle: asks <see cref="ChannelManagerOptions.SelectRefundChannels"/>
        /// which idle channels to release, removes them from tracking and raises
        /// <see cref="ChannelManagerOptions.OnRefund"/> so the host can execute the cooperative refund.
        /// Channels with unclaimed or unsettled amounts are never refunded.
        /// </summary>
        public Task RefundIdleChannelsAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref refundCycleRunning, 1) == 1)
                return Task.CompletedTask;

            try
            {
                var selector = options.SelectRefundChannels;
                if (selector == null)
                    return Task.CompletedTask;

                var snapshot = Channels;
                var selected = selector(snapshot, new RefundContext { Now = DateTimeOffset.UtcNow })?.ToList();
                if (selected == null || selected.Count == 0)
                    return Task.CompletedTask;

                foreach (var state in selected)
                {
                    if (!channels.TryGetValue(state.ChannelId, out var channel))
                        continue;

                    lock (channel.SyncRoot)
                    {
                        if (channel.State.PendingAmount != BigInteger.Zero || channel.State.ClaimedAmount != BigInteger.Zero)
                        {
                            logger.LogDebug("Skipping refund of channel {Channel}; it has unclaimed or unsettled amounts", state.ChannelId);
                            continue;
                        }
                        channels.TryRemove(state.ChannelId, out _);
                    }

                    logger.LogInformation("Refunded channel {Channel} (unused balance {Balance})", state.ChannelId, channel.State.Balance);
                    try
                    {
                        options.OnRefund?.Invoke(new RefundResult(state.ChannelId, channel.State.Balance.ToString()));
                    }
                    catch (Exception ex)
                    {
                        ReportError(ex);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref refundCycleRunning, 0);
            }

            return Task.CompletedTask;
        }

        private static void Requeue(Channel channel, List<ChannelVoucher> batch, BigInteger batchAmount)
        {
            lock (channel.SyncRoot)
            {
                foreach (var voucher in batch)
                {
                    channel.PendingVouchers.Enqueue(voucher);
                }
                channel.State.PendingAmount += batchAmount;
                channel.State.PendingVoucherCount = channel.PendingVouchers.Count;
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
