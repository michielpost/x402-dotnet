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
