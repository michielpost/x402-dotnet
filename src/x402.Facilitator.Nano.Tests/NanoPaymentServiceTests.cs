using System.Numerics;
using x402.Core.Enums;
using x402.Core.Models.Facilitator;
using x402.Core.Models.v2;

namespace x402.Facilitator.Nano.Tests;

[TestFixture]
public class NanoPaymentServiceTests
{
    private static NanoPaymentService Service(params INanoRpcClient[] nodes) =>
        new(nodes, new InMemoryNanoClaimStore());

    private static FakeNanoRpcClient NodeWith(string blockInfoJson) =>
        new FakeNanoRpcClient().With(NanoFixtures.BlockHash, blockInfoJson);

    [Test]
    public void RawPerXno_IsTenToTheThirtieth()
    {
        Assert.That(NanoNetworks.RawPerXno.ToString(), Is.EqualTo("1" + new string('0', 30)));
    }

    [Test]
    public async Task Verify_ConfirmedSendOfExactAmountToPayTo_IsValid()
    {
        var service = Service(NodeWith(NanoFixtures.BlockInfoJson()), NodeWith(NanoFixtures.BlockInfoJson()));

        var result = await service.VerifyPayment(NanoFixtures.Header(NanoFixtures.BlockHash), NanoFixtures.Requirements());

        Assert.That(result.IsValid, Is.True, result.InvalidReason);
        Assert.That(result.Payer, Is.EqualTo(NanoFixtures.Payer));
    }

    [Test]
    public async Task Verify_PayerComesFromTheLedger_WhenPayloadNamesNone()
    {
        var service = Service(NodeWith(NanoFixtures.BlockInfoJson()));

        var result = await service.VerifyPayment(NanoFixtures.Header(NanoFixtures.BlockHash, from: ""), NanoFixtures.Requirements());

        Assert.That(result.IsValid, Is.True, result.InvalidReason);
        Assert.That(result.Payer, Is.EqualTo(NanoFixtures.Payer));
    }

    [Test]
    public async Task Verify_PayloadNamingAnotherPayer_IsRefused()
    {
        var service = Service(NodeWith(NanoFixtures.BlockInfoJson()));

        var result = await service.VerifyPayment(
            NanoFixtures.Header(NanoFixtures.BlockHash, from: NanoFixtures.Stranger),
            NanoFixtures.Requirements());

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.InvalidReason, Is.EqualTo(FacilitatorErrorCodes.InvalidPayload));
    }

    [Test]
    public async Task Verify_LowercaseHashAndOldAccountPrefix_AreAccepted()
    {
        var oldSpelling = "xrb_" + NanoFixtures.PayTo["nano_".Length..];
        var node = NodeWith(NanoFixtures.BlockInfoJson(destination: oldSpelling.ToUpperInvariant()));
        var service = Service(node);

        var result = await service.VerifyPayment(
            NanoFixtures.Header(NanoFixtures.BlockHash.ToLowerInvariant()),
            NanoFixtures.Requirements());

        Assert.That(result.IsValid, Is.True, result.InvalidReason);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not-a-hash")]
    [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 63 characters
    [TestCase("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")] // 64, not hex
    public async Task Verify_MissingOrMalformedBlockHash_IsRefusedWithoutAskingANode(string? blockHash)
    {
        var node = NodeWith(NanoFixtures.BlockInfoJson());
        var service = Service(node);

        var result = await service.VerifyPayment(NanoFixtures.Header(blockHash), NanoFixtures.Requirements());

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.InvalidReason, Is.EqualTo(NanoErrorCodes.InvalidBlockHash));
        Assert.That(node.Calls, Is.Zero);
    }

    [Test]
    public async Task Verify_UnknownBlock_IsRefused()
    {
        var service = Service(new FakeNanoRpcClient());

        var result = await service.VerifyPayment(NanoFixtures.Header(NanoFixtures.BlockHash), NanoFixtures.Requirements());

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.InvalidReason, Is.EqualTo(FacilitatorErrorCodes.InvalidTransactionState));
    }

    [TestCase("false")]
    [TestCase("")]
    public async Task Verify_UnconfirmedBlock_IsRefused(string confirmed)
    {
        var service = Service(NodeWith(NanoFixtures.BlockInfoJson(confirmed: confirmed)));

        var result = await service.VerifyPayment(NanoFixtures.Header(NanoFixtures.BlockHash), NanoFixtures.Requirements());

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.InvalidReason, Is.EqualTo(FacilitatorErrorCodes.InvalidTransactionState));
    }

    [TestCase("receive")]
    [TestCase("change")]
    [TestCase("epoch")]
    public async Task Verify_BlockThatIsNotASend_IsRefused(string subtype)
    {
        var service = Service(NodeWith(NanoFixtures.BlockInfoJson(subtype: subtype)));

        var result = await service.VerifyPayment(NanoFixtures.Header(NanoFixtures.BlockHash), NanoFixtures.Requirements());

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.InvalidReason, Is.EqualTo(FacilitatorErrorCodes.InvalidTransactionState));
    }

    [Test]
    public async Task Verify_SendToAnotherAccount_IsRefused()
    {
        var service = Service(NodeWith(NanoFixtures.BlockInfoJson(destination: NanoFixtures.Stranger)));

        var result = await service.VerifyPayment(NanoFixtures.Header(NanoFixtures.BlockHash), NanoFixtures.Requirements());

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.InvalidReason, Is.EqualTo(NanoErrorCodes.RecipientMismatch));
    }

    [TestCase(-1)] // one raw short
    [TestCase(1)] // one raw over: "exact" means exact
    public async Task Verify_AmountOffByOneRaw_IsRefused(int difference)
    {
        var service = Service(NodeWith(NanoFixtures.BlockInfoJson(amount: NanoFixtures.Price + difference)));

        var result = await service.VerifyPayment(NanoFixtures.Header(NanoFixtures.BlockHash), NanoFixtures.Requirements());

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.InvalidReason, Is.EqualTo(NanoErrorCodes.AmountMismatch));
    }

    [Test]
    public async Task Verify_AmountLargerThanAnyFixedWidthInteger_IsComparedExactly()
    {
        // 100,000 XNO in raw needs 117 bits.
        var large = NanoNetworks.RawPerXno * 100_000;
        Assert.That(large, Is.GreaterThan(new BigInteger(ulong.MaxValue)));

        var requirements = NanoFixtures.Requirements(large);
        var service = Service(NodeWith(NanoFixtures.BlockInfoJson(amount: large)));

        var result = await service.VerifyPayment(NanoFixtures.Header(NanoFixtures.BlockHash, accepted: requirements), requirements);

        Assert.That(result.IsValid, Is.True, result.InvalidReason);

        var offByOne = Service(NodeWith(NanoFixtures.BlockInfoJson(amount: large - 1)));
        var refused = await offByOne.VerifyPayment(NanoFixtures.Header(NanoFixtures.BlockHash, accepted: requirements), requirements);

        Assert.That(refused.IsValid, Is.False);
        Assert.That(refused.InvalidReason, Is.EqualTo(NanoErrorCodes.AmountMismatch));
    }

    [Test]
    public async Task Verify_NodeThatCannotAnswer_IsRefused()
    {
        var healthy = NodeWith(NanoFixtures.BlockInfoJson());
        var broken = new FakeNanoRpcClient { Throw = new NanoRpcException("node down") };
        var service = Service(healthy, broken);

        var result = await service.VerifyPayment(NanoFixtures.Header(NanoFixtures.BlockHash), NanoFixtures.Requirements());

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.InvalidReason, Is.EqualTo(FacilitatorErrorCodes.UnexpectedVerifyError));
    }

    [Test]
    public async Task Verify_NodesThatDisagree_AreRefused()
    {
        var first = NodeWith(NanoFixtures.BlockInfoJson());
        var second = NodeWith(NanoFixtures.BlockInfoJson(confirmed: "false"));
        var service = Service(first, second);

        var result = await service.VerifyPayment(NanoFixtures.Header(NanoFixtures.BlockHash), NanoFixtures.Requirements());

        Assert.That(result.IsValid, Is.False);
        Assert.That(first.Calls, Is.EqualTo(1));
        Assert.That(second.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task Verify_OtherScheme_IsRefused()
    {
        var requirements = NanoFixtures.Requirements();
        requirements.Scheme = PaymentScheme.Upto;
        var service = Service(NodeWith(NanoFixtures.BlockInfoJson()));

        var result = await service.VerifyPayment(NanoFixtures.Header(NanoFixtures.BlockHash), requirements);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.InvalidReason, Is.EqualTo(FacilitatorErrorCodes.UnsupportedScheme));
    }

    [Test]
    public async Task Verify_OtherNetwork_IsRefused()
    {
        var requirements = NanoFixtures.Requirements();
        requirements.Network = "nano:testnet";
        var service = Service(NodeWith(NanoFixtures.BlockInfoJson()));

        var result = await service.VerifyPayment(NanoFixtures.Header(NanoFixtures.BlockHash, accepted: requirements), requirements);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.InvalidReason, Is.EqualTo(FacilitatorErrorCodes.InvalidNetwork));
    }

    [Test]
    public async Task Verify_OtherAsset_IsRefused()
    {
        var requirements = NanoFixtures.Requirements();
        requirements.Asset = "USDC";
        var service = Service(NodeWith(NanoFixtures.BlockInfoJson()));

        var result = await service.VerifyPayment(NanoFixtures.Header(NanoFixtures.BlockHash, accepted: requirements), requirements);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.InvalidReason, Is.EqualTo(FacilitatorErrorCodes.InvalidPaymentRequirements));
    }

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("0.001")]
    [TestCase("1e27")]
    [TestCase("")]
    public async Task Verify_AmountThatIsNotAPositiveIntegerInRaw_IsRefused(string amount)
    {
        var requirements = NanoFixtures.Requirements();
        requirements.Amount = amount;
        var service = Service(NodeWith(NanoFixtures.BlockInfoJson()));

        var result = await service.VerifyPayment(NanoFixtures.Header(NanoFixtures.BlockHash, accepted: requirements), requirements);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.InvalidReason, Is.EqualTo(FacilitatorErrorCodes.InvalidPaymentRequirements));
    }

    [Test]
    public async Task Verify_PayloadAcceptedForOtherTerms_IsRefused()
    {
        var accepted = NanoFixtures.Requirements(NanoFixtures.Price - 1);
        var service = Service(NodeWith(NanoFixtures.BlockInfoJson()));

        var result = await service.VerifyPayment(
            NanoFixtures.Header(NanoFixtures.BlockHash, accepted: accepted),
            NanoFixtures.Requirements());

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.InvalidReason, Is.EqualTo(FacilitatorErrorCodes.InvalidPaymentRequirements));
    }

    [Test]
    public async Task Settle_ReturnsTheBlockHashAsTransaction()
    {
        var service = Service(NodeWith(NanoFixtures.BlockInfoJson()));

        var result = await service.SettlePayment(
            NanoFixtures.Header(NanoFixtures.BlockHash.ToLowerInvariant()),
            NanoFixtures.Requirements());

        Assert.That(result.Success, Is.True, result.ErrorReason);
        Assert.That(result.Transaction, Is.EqualTo(NanoFixtures.BlockHash));
        Assert.That(result.Network, Is.EqualTo(NanoNetworks.Mainnet));
        Assert.That(result.Payer, Is.EqualTo(NanoFixtures.Payer));
    }

    [Test]
    public async Task Settle_SameBlockTwice_SettlesOnce()
    {
        var service = Service(NodeWith(NanoFixtures.BlockInfoJson()));

        var first = await service.SettlePayment(NanoFixtures.Header(NanoFixtures.BlockHash), NanoFixtures.Requirements());
        var replay = await service.SettlePayment(NanoFixtures.Header(NanoFixtures.BlockHash.ToLowerInvariant()), NanoFixtures.Requirements());
        var verifyAfter = await service.VerifyPayment(NanoFixtures.Header(NanoFixtures.BlockHash), NanoFixtures.Requirements());

        Assert.That(first.Success, Is.True, first.ErrorReason);
        Assert.That(replay.Success, Is.False);
        Assert.That(replay.ErrorReason, Is.EqualTo(NanoErrorCodes.BlockAlreadySettled));
        Assert.That(replay.Transaction, Is.Empty);
        Assert.That(verifyAfter.IsValid, Is.False);
        Assert.That(verifyAfter.InvalidReason, Is.EqualTo(NanoErrorCodes.BlockAlreadySettled));
    }

    [Test]
    public async Task Settle_SameBlockConcurrently_SettlesOnce()
    {
        var service = Service(NodeWith(NanoFixtures.BlockInfoJson()));

        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
            service.SettlePayment(NanoFixtures.Header(NanoFixtures.BlockHash), NanoFixtures.Requirements()))));

        Assert.That(results.Count(r => r.Success), Is.EqualTo(1));
    }

    [Test]
    public async Task Settle_InvalidPayment_DoesNotClaimTheBlock()
    {
        var claims = new InMemoryNanoClaimStore();
        var unconfirmed = new NanoPaymentService([NodeWith(NanoFixtures.BlockInfoJson(confirmed: "false"))], claims);

        var result = await unconfirmed.SettlePayment(NanoFixtures.Header(NanoFixtures.BlockHash), NanoFixtures.Requirements());

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorReason, Is.EqualTo(FacilitatorErrorCodes.InvalidTransactionState));
        Assert.That(claims.IsClaimed(NanoFixtures.BlockHash), Is.False);

        // Once the block confirms, the same payment settles.
        var confirmed = new NanoPaymentService([NodeWith(NanoFixtures.BlockInfoJson())], claims);
        var later = await confirmed.SettlePayment(NanoFixtures.Header(NanoFixtures.BlockHash), NanoFixtures.Requirements());

        Assert.That(later.Success, Is.True, later.ErrorReason);
    }

    [Test]
    public void Constructor_WithoutNodes_Throws()
    {
        Assert.Throws<ArgumentException>(() => new NanoPaymentService([], new InMemoryNanoClaimStore()));
    }

    [Test]
    public void Payload_BlockHash_RoundTripsThroughTheHeader_AndIsOmittedWhenNull()
    {
        var header = PaymentPayloadHeader.FromHeader(NanoFixtures.Header(NanoFixtures.BlockHash).ToBase64Header());
        Assert.That(header.Payload.BlockHash, Is.EqualTo(NanoFixtures.BlockHash));

        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(NanoFixtures.Header(null).ToBase64Header()));
        Assert.That(json, Does.Not.Contain("blockHash"));
    }
}
