using Microsoft.Extensions.Logging.Abstractions;
using System.Numerics;
using x402.Channels;
using x402.Core.Enums;
using x402.Core.Models.Facilitator;
using x402.Core.Models.v2;

namespace x402.Tests
{
    [TestFixture]
    public class ChannelManagerTests
    {
        private static PaymentRequirements CreateRequirements(string maxAmount = "1000")
        {
            return new PaymentRequirements
            {
                Scheme = PaymentScheme.BatchSettlement,
                Network = "eip155:84532",
                Amount = maxAmount,
                Asset = "USDC",
                PayTo = "0x0000000000000000000000000000000000000001",
            };
        }

        private static PaymentPayloadHeader CreatePayload(string authorizedValue = "1000", string payer = "0xabc")
        {
            return new PaymentPayloadHeader
            {
                X402Version = 2,
                Accepted = CreateRequirements(),
                Payload = new Payload
                {
                    Authorization = new Authorization
                    {
                        From = payer,
                        To = "0x0000000000000000000000000000000000000001",
                        Value = authorizedValue,
                        ValidAfter = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                        ValidBefore = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds().ToString(),
                    }
                }
            };
        }

        private static ChannelManager CreateManager(FakeFacilitatorClient facilitator)
        {
            return new ChannelManager(new NullLogger<ChannelManager>(), facilitator);
        }

        private static ChannelManagerOptions ManualCycleOptions(ChannelManagerOptions? template = null)
        {
            // Zero intervals: options are applied but no background cycles run,
            // so tests can invoke cycles manually and deterministically.
            var options = template ?? new ChannelManagerOptions();
            options.ClaimIntervalSecs = 0;
            options.SettleIntervalSecs = 0;
            options.RefundIntervalSecs = 0;
            return options;
        }

        [Test]
        public void RecordVoucher_UpdatesChannelState()
        {
            var facilitator = new FakeFacilitatorClient();
            var manager = CreateManager(facilitator);

            var state = manager.RecordVoucher(CreatePayload("1000"), CreateRequirements(), new BigInteger(75));

            Assert.That(manager.Channels, Has.Count.EqualTo(1));
            Assert.That(state.PendingAmount, Is.EqualTo(new BigInteger(75)));
            Assert.That(state.TotalCharged, Is.EqualTo(new BigInteger(75)));
            Assert.That(state.AuthorizedAmount, Is.EqualTo(new BigInteger(1000)));
            Assert.That(state.Balance, Is.EqualTo(new BigInteger(925)));
            Assert.That(state.PendingVoucherCount, Is.EqualTo(1));
            Assert.That(state.Payer, Is.EqualTo("0xabc"));
        }

        [Test]
        public void RecordVoucher_SamePayer_ReusesChannel()
        {
            var facilitator = new FakeFacilitatorClient();
            var manager = CreateManager(facilitator);

            manager.RecordVoucher(CreatePayload(), CreateRequirements(), new BigInteger(10));
            manager.RecordVoucher(CreatePayload(), CreateRequirements(), new BigInteger(20));
            manager.RecordVoucher(CreatePayload(payer: "0xother"), CreateRequirements(), new BigInteger(5));

            Assert.That(manager.Channels, Has.Count.EqualTo(2));
            var channel = manager.Channels.Single(c => c.Payer == "0xabc");
            Assert.That(channel.PendingAmount, Is.EqualTo(new BigInteger(30)));
            Assert.That(channel.PendingVoucherCount, Is.EqualTo(2));
        }

        [Test]
        public void RecordVoucher_NegativeAmount_Throws()
        {
            var manager = CreateManager(new FakeFacilitatorClient());

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                manager.RecordVoucher(CreatePayload(), CreateRequirements(), new BigInteger(-1)));
        }

        [Test]
        public async Task ClaimPendingAsync_BatchesVouchersIntoSingleSettlement()
        {
            var facilitator = new FakeFacilitatorClient();
            var manager = CreateManager(facilitator);
            ClaimResult? claimResult = null;
            manager.Start(ManualCycleOptions(new ChannelManagerOptions
            {
                OnClaim = r => claimResult = r
            }));

            manager.RecordVoucher(CreatePayload(), CreateRequirements(), new BigInteger(10));
            manager.RecordVoucher(CreatePayload(), CreateRequirements(), new BigInteger(20));
            manager.RecordVoucher(CreatePayload(), CreateRequirements(), new BigInteger(30));

            await manager.ClaimPendingAsync();

            Assert.That(facilitator.SettleCallCount, Is.EqualTo(1), "one facilitator settlement per channel per claim cycle");
            Assert.That(facilitator.LastSettlementAmount, Is.EqualTo("60"));
            Assert.That(claimResult, Is.Not.Null);
            Assert.That(claimResult!.Vouchers, Is.EqualTo(3));
            Assert.That(claimResult.Transaction, Is.EqualTo("0xabc"));

            var channel = manager.Channels.Single();
            Assert.That(channel.PendingAmount, Is.EqualTo(BigInteger.Zero));
            Assert.That(channel.PendingVoucherCount, Is.EqualTo(0));
            Assert.That(channel.ClaimedAmount, Is.EqualTo(new BigInteger(60)));

            await manager.StopAsync();
        }

        [Test]
        public async Task ClaimPendingAsync_RespectsMaxClaimsPerBatch()
        {
            var facilitator = new FakeFacilitatorClient();
            var manager = CreateManager(facilitator);
            manager.Start(ManualCycleOptions(new ChannelManagerOptions { MaxClaimsPerBatch = 2 }));

            manager.RecordVoucher(CreatePayload(), CreateRequirements(), new BigInteger(10));
            manager.RecordVoucher(CreatePayload(), CreateRequirements(), new BigInteger(20));
            manager.RecordVoucher(CreatePayload(), CreateRequirements(), new BigInteger(30));

            await manager.ClaimPendingAsync();

            Assert.That(facilitator.LastSettlementAmount, Is.EqualTo("30"), "only the first two vouchers are claimed");
            var channel = manager.Channels.Single();
            Assert.That(channel.PendingVoucherCount, Is.EqualTo(1));
            Assert.That(channel.PendingAmount, Is.EqualTo(new BigInteger(30)));

            await manager.ClaimPendingAsync();

            Assert.That(channel.PendingVoucherCount, Is.EqualTo(0));
            Assert.That(channel.ClaimedAmount, Is.EqualTo(new BigInteger(60)));

            await manager.StopAsync();
        }

        [Test]
        public async Task ClaimPendingAsync_FacilitatorFailure_RequeuesVouchers_AndReportsError()
        {
            var facilitator = new FakeFacilitatorClient
            {
                SettleAsyncImpl = (_, _) => Task.FromResult(new SettlementResponse { Success = false, ErrorReason = "nope" })
            };
            var manager = CreateManager(facilitator);
            Exception? reported = null;
            manager.Start(ManualCycleOptions(new ChannelManagerOptions { OnError = e => reported = e }));

            manager.RecordVoucher(CreatePayload(), CreateRequirements(), new BigInteger(10));
            manager.RecordVoucher(CreatePayload(), CreateRequirements(), new BigInteger(20));

            await manager.ClaimPendingAsync();

            Assert.That(reported, Is.Not.Null);
            var channel = manager.Channels.Single();
            Assert.That(channel.PendingAmount, Is.EqualTo(new BigInteger(30)), "failed claims are requeued");
            Assert.That(channel.PendingVoucherCount, Is.EqualTo(2));
            Assert.That(channel.ClaimedAmount, Is.EqualTo(BigInteger.Zero));

            await manager.StopAsync();
        }

        [Test]
        public async Task SettleClaimedAsync_InvokesOnSettle_AndResetsClaimedAmount()
        {
            var facilitator = new FakeFacilitatorClient();
            var manager = CreateManager(facilitator);
            SettleResult? settleResult = null;
            manager.Start(ManualCycleOptions(new ChannelManagerOptions { OnSettle = r => settleResult = r }));

            manager.RecordVoucher(CreatePayload(), CreateRequirements(), new BigInteger(40));
            await manager.ClaimPendingAsync();
            await manager.SettleClaimedAsync();

            Assert.That(settleResult, Is.Not.Null);
            Assert.That(settleResult!.Amount, Is.EqualTo("40"));
            Assert.That(manager.Channels.Single().ClaimedAmount, Is.EqualTo(BigInteger.Zero));

            await manager.StopAsync();
        }

        [Test]
        public async Task RefundIdleChannels_RemovesSelectedChannels_AndInvokesOnRefund()
        {
            var facilitator = new FakeFacilitatorClient();
            var manager = CreateManager(facilitator);
            RefundResult? refundResult = null;
            manager.Start(ManualCycleOptions(new ChannelManagerOptions
            {
                SelectRefundChannels = (channels, ctx) => channels.Where(c => c.Balance > BigInteger.Zero),
                OnRefund = r => refundResult = r
            }));

            manager.RecordVoucher(CreatePayload("1000"), CreateRequirements(), new BigInteger(100));
            await manager.ClaimPendingAsync();
            await manager.SettleClaimedAsync();

            await manager.RefundIdleChannelsAsync();

            Assert.That(refundResult, Is.Not.Null);
            Assert.That(refundResult!.RefundedAmount, Is.EqualTo("900"));
            Assert.That(manager.Channels, Is.Empty);

            await manager.StopAsync();
        }

        [Test]
        public async Task RefundIdleChannels_SkipsChannelsWithPendingVouchers()
        {
            var facilitator = new FakeFacilitatorClient();
            var manager = CreateManager(facilitator);
            var refunded = new List<RefundResult>();
            manager.Start(ManualCycleOptions(new ChannelManagerOptions
            {
                SelectRefundChannels = (channels, ctx) => channels,
                OnRefund = refunded.Add
            }));

            manager.RecordVoucher(CreatePayload(), CreateRequirements(), new BigInteger(10));

            await manager.RefundIdleChannelsAsync();

            Assert.That(refunded, Is.Empty, "channels with unclaimed vouchers must not be refunded");
            Assert.That(manager.Channels, Has.Count.EqualTo(1));

            await manager.StopAsync();
        }

        [Test]
        public async Task RefundIdleChannels_RefundCallbackThrows_KeepsChannelForRetry()
        {
            var manager = CreateManager(new FakeFacilitatorClient());
            var refunds = new List<RefundResult>();
            Exception? reported = null;
            int attempts = 0;
            manager.Start(ManualCycleOptions(new ChannelManagerOptions
            {
                SelectRefundChannels = (channels, _) => channels,
                OnError = e => reported = e,
                OnRefund = r =>
                {
                    attempts++;
                    if (attempts == 1)
                        throw new InvalidOperationException("transient refund failure");
                    refunds.Add(r);
                }
            }));

            manager.RecordVoucher(CreatePayload("1000"), CreateRequirements(), new BigInteger(100));
            await manager.ClaimPendingAsync();
            await manager.SettleClaimedAsync();

            await manager.RefundIdleChannelsAsync();

            Assert.That(reported, Is.Not.Null);
            Assert.That(manager.Channels, Has.Count.EqualTo(1), "a failed refund callback must keep the channel tracked");

            await manager.RefundIdleChannelsAsync();

            Assert.That(refunds, Has.Count.EqualTo(1), "the next cycle retries the refund");
            Assert.That(refunds[0].RefundedAmount, Is.EqualTo("900"));
            Assert.That(manager.Channels, Is.Empty, "the channel is removed once the refund succeeds");

            await manager.StopAsync();
        }

        [Test]
        public async Task RefundIdleChannels_AsyncCallbackReportingFailure_KeepsChannelForRetry()
        {
            var manager = CreateManager(new FakeFacilitatorClient());
            var completed = new List<RefundResult>();
            int attempts = 0;
            manager.Start(ManualCycleOptions(new ChannelManagerOptions
            {
                SelectRefundChannels = (channels, _) => channels,
                OnRefundAsync = (result, _) =>
                {
                    attempts++;
                    if (attempts == 1)
                        return Task.FromResult(false); // not refunded yet, e.g. the client never co-signed

                    completed.Add(result);
                    return Task.FromResult(true);
                }
            }));

            manager.RecordVoucher(CreatePayload("1000"), CreateRequirements(), new BigInteger(100));
            await manager.ClaimPendingAsync();
            await manager.SettleClaimedAsync();

            await manager.RefundIdleChannelsAsync();

            Assert.That(manager.Channels, Has.Count.EqualTo(1), "an unsuccessful async refund must keep the channel tracked");

            await manager.RefundIdleChannelsAsync();

            Assert.That(completed, Has.Count.EqualTo(1));
            Assert.That(manager.Channels, Is.Empty);

            await manager.StopAsync();
        }

        [Test]
        public async Task RefundIdleChannels_DoesNotRefundChannelWhileClaimIsInFlight()
        {
            var gate = new TaskCompletionSource<SettlementResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var facilitator = new FakeFacilitatorClient { SettleAsyncImpl = (_, _) => gate.Task };
            var manager = CreateManager(facilitator);
            var refunds = new List<RefundResult>();
            SettleResult? settleResult = null;
            manager.Start(ManualCycleOptions(new ChannelManagerOptions
            {
                SelectRefundChannels = (channels, _) => channels,
                OnRefund = refunds.Add,
                OnSettle = r => settleResult = r
            }));

            var state = manager.RecordVoucher(CreatePayload("1000"), CreateRequirements(), new BigInteger(100));
            var claim = manager.ClaimPendingAsync();

            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (facilitator.SettleCallCount == 0 && DateTime.UtcNow < deadline)
                    await Task.Delay(5);

                Assert.That(facilitator.SettleCallCount, Is.EqualTo(1), "the claim should be awaiting the facilitator");
                Assert.That(state.PendingAmount, Is.EqualTo(new BigInteger(100)), "an in-flight batch still counts as pending");

                await manager.RefundIdleChannelsAsync();

                Assert.That(refunds, Is.Empty, "a channel with a claim in flight must not be refunded");
                Assert.That(manager.Channels, Has.Count.EqualTo(1));
            }
            finally
            {
                gate.TrySetResult(new SettlementResponse { Success = true, Transaction = "0xclaim", Network = "eip155:84532" });
            }

            await claim;
            await manager.SettleClaimedAsync();

            Assert.That(state.ClaimedAmount, Is.EqualTo(BigInteger.Zero));
            Assert.That(settleResult, Is.Not.Null);
            Assert.That(settleResult!.Amount, Is.EqualTo("100"), "the claim is still settled after the refund attempt");

            await manager.StopAsync();
        }

        [Test]
        public async Task RecordVoucher_DuringRefund_StartsANewChannelGeneration()
        {
            var refundStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var refundGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var facilitator = new FakeFacilitatorClient();
            var manager = CreateManager(facilitator);
            var refunds = new List<RefundResult>();
            manager.Start(ManualCycleOptions(new ChannelManagerOptions
            {
                SelectRefundChannels = (channels, _) => channels,
                OnRefundAsync = async (result, _) =>
                {
                    refunds.Add(result);
                    refundStarted.TrySetResult(true);
                    return await refundGate.Task;
                }
            }));

            manager.RecordVoucher(CreatePayload("1000"), CreateRequirements(), new BigInteger(100));
            await manager.ClaimPendingAsync();
            await manager.SettleClaimedAsync();

            var refundCycle = manager.RefundIdleChannelsAsync();
            var started = await Task.WhenAny(refundStarted.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.That(started, Is.EqualTo(refundStarted.Task), "the refund callback should have been invoked");

            // A request arrives while the refund is in flight.
            var state = manager.RecordVoucher(CreatePayload("1000"), CreateRequirements(), new BigInteger(30));

            refundGate.TrySetResult(true);
            await refundCycle;

            Assert.That(refunds, Has.Count.EqualTo(1));
            Assert.That(refunds[0].RefundedAmount, Is.EqualTo("900"));
            Assert.That(manager.Channels, Has.Count.EqualTo(1), "vouchers recorded during a refund must stay tracked");
            Assert.That(state.PendingAmount, Is.EqualTo(new BigInteger(30)));
            Assert.That(state.TotalCharged, Is.EqualTo(new BigInteger(30)), "the new generation only tracks its own vouchers");

            await manager.ClaimPendingAsync();

            Assert.That(facilitator.SettleCallCount, Is.EqualTo(2));
            Assert.That(facilitator.LastSettlementAmount, Is.EqualTo("30"), "the voucher recorded during the refund is still claimable");

            await manager.StopAsync();
        }

        [Test]
        public async Task SettleClaimedAsync_SettleCallbackThrows_KeepsClaimedAmountForRetry()
        {
            var manager = CreateManager(new FakeFacilitatorClient());
            int attempts = 0;
            SettleResult? settled = null;
            Exception? reported = null;
            manager.Start(ManualCycleOptions(new ChannelManagerOptions
            {
                OnError = e => reported = e,
                OnSettle = r =>
                {
                    attempts++;
                    if (attempts == 1)
                        throw new InvalidOperationException("transient settle failure");
                    settled = r;
                }
            }));

            var state = manager.RecordVoucher(CreatePayload(), CreateRequirements(), new BigInteger(40));
            await manager.ClaimPendingAsync();

            await manager.SettleClaimedAsync();

            Assert.That(reported, Is.Not.Null);
            Assert.That(state.ClaimedAmount, Is.EqualTo(new BigInteger(40)), "a failed settle callback must not drop the claimed amount");

            await manager.SettleClaimedAsync();

            Assert.That(settled, Is.Not.Null);
            Assert.That(settled!.Amount, Is.EqualTo("40"));
            Assert.That(state.ClaimedAmount, Is.EqualTo(BigInteger.Zero));

            await manager.StopAsync();
        }

        [Test]
        public async Task StopAsync_WithFlush_ClaimsAndSettlesRemainingVouchers()
        {
            var facilitator = new FakeFacilitatorClient();
            var manager = CreateManager(facilitator);
            SettleResult? settleResult = null;
            manager.Start(ManualCycleOptions(new ChannelManagerOptions { OnSettle = r => settleResult = r }));

            manager.RecordVoucher(CreatePayload(), CreateRequirements(), new BigInteger(15));

            await manager.StopAsync(flush: true);

            Assert.That(facilitator.SettleCallCount, Is.EqualTo(1));
            Assert.That(facilitator.LastSettlementAmount, Is.EqualTo("15"));
            Assert.That(settleResult, Is.Not.Null);
            Assert.That(manager.Channels.Single().PendingAmount, Is.EqualTo(BigInteger.Zero));
        }

        [Test]
        public async Task Start_Twice_Throws_AndStopAllowsRestart()
        {
            var manager = CreateManager(new FakeFacilitatorClient());
            manager.Start(new ChannelManagerOptions { ClaimIntervalSecs = 3600, SettleIntervalSecs = 3600, RefundIntervalSecs = 3600 });

            Assert.That(manager.IsRunning, Is.True);
            Assert.Throws<InvalidOperationException>(() => manager.Start(new ChannelManagerOptions()));

            await manager.StopAsync();
            Assert.That(manager.IsRunning, Is.False);

            manager.Start(new ChannelManagerOptions { ClaimIntervalSecs = 3600, SettleIntervalSecs = 3600, RefundIntervalSecs = 3600 });
            Assert.That(manager.IsRunning, Is.True);
            await manager.StopAsync();
        }

        [Test]
        public async Task BackgroundCycle_ClaimsAutomatically()
        {
            var facilitator = new FakeFacilitatorClient();
            var manager = CreateManager(facilitator);
            var claimed = new TaskCompletionSource<ClaimResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.Start(new ChannelManagerOptions
            {
                ClaimIntervalSecs = 1,
                SettleIntervalSecs = 3600,
                RefundIntervalSecs = 3600,
                OnClaim = r => claimed.TrySetResult(r)
            });

            manager.RecordVoucher(CreatePayload(), CreateRequirements(), new BigInteger(10));

            var completed = await Task.WhenAny(claimed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            await manager.StopAsync();

            Assert.That(completed, Is.EqualTo(claimed.Task), "background claim cycle should have run within 10 seconds");
            Assert.That(facilitator.LastSettlementAmount, Is.EqualTo("10"));
        }
    }
}
