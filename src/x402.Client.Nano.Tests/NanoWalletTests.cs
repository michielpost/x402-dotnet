using System.Numerics;
using x402.Client.Nano;
using x402.Core.Enums;
using x402.Core.Models.v2;
using x402.Facilitator.Nano;
using ClientNetworks = x402.Client.Nano.NanoNetworks;

namespace x402.Client.Nano.Tests;

/// <summary>
/// No keys or seeds anywhere: the sender is a fake, the block hashes are a repeated
/// hex digit and the accounts are public, well-known addresses.
/// </summary>
public class NanoWalletTests
{
    // The burn account and the genesis account.
    private const string PayTo = "nano_1111111111111111111111111111111111111111111111111111hifc8npp";
    private const string Payer = "nano_3t6k35gi95xu6tergt6p69ck76ogmitsa8mnijtpxm9fkcm736xtoncuohr3";

    private static readonly string BlockHash = new('A', 64);
    private static readonly BigInteger RawPerXno = BigInteger.Pow(10, 30);

    // 0.001 XNO, far beyond what a long can hold.
    private static readonly BigInteger Price = RawPerXno / 1000;

    private sealed class FakeSender : INanoSender
    {
        public readonly List<(string Destination, BigInteger Amount, string Id)> Sends = new();
        public string Result { get; set; } = new('a', 64);

        public Task<string> SendAsync(string destination, BigInteger amountRaw, string id, CancellationToken cancellationToken = default)
        {
            Sends.Add((destination, amountRaw, id));
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeRpc : INanoRpcClient
    {
        public NanoBlockInfo? Block { get; set; }

        public Task<NanoBlockInfo?> GetBlockInfoAsync(string blockHash, CancellationToken cancellationToken = default)
            => Task.FromResult(Block);
    }

    private static PaymentRequirements NanoRequirement(string? amount = null, string payTo = PayTo, string network = ClientNetworks.Mainnet, string asset = "XNO") => new()
    {
        Scheme = PaymentScheme.Exact,
        Network = network,
        Amount = amount ?? Price.ToString(),
        Asset = asset,
        PayTo = payTo
    };

    private static PaymentRequirements EvmRequirement() => new()
    {
        Scheme = PaymentScheme.Exact,
        Network = "eip155:8453",
        Amount = "10000",
        Asset = "0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913",
        PayTo = "0x1111111111111111111111111111111111111111"
    };

    private static PaymentRequiredResponse Offer(params PaymentRequirements[] accepts) => new()
    {
        X402Version = 2,
        Resource = new() { Url = "https://unit.test/resource" },
        Accepts = accepts.ToList()
    };

    private static NanoWallet Wallet(FakeSender sender) => new(Payer, sender)
    {
        Allowance = new NanoAllowance { TotalAllowanceRaw = Price * 3, MaxPerRequestAllowanceRaw = Price }
    };

    [Test]
    public async Task UppercaseAccounts_AreNormalizedToLowercase()
    {
        var sender = new FakeSender();
        var wallet = new NanoWallet(Payer.ToUpperInvariant(), sender) { IgnoreAllowances = true };

        Assert.That(wallet.Account, Is.EqualTo(Payer));

        var header = await wallet.CreateHeaderAsync(NanoRequirement(payTo: PayTo.ToUpperInvariant()));

        Assert.That(sender.Sends.Single().Destination, Is.EqualTo(PayTo));
        Assert.That(header.Payload!.Authorization!.From, Is.EqualTo(Payer));
    }

    [Test]
    public async Task SelectPayment_PicksNanoExact_FromMixedOffer()
    {
        var nano = NanoRequirement();
        var selected = await Wallet(new FakeSender()).SelectPaymentAsync(Offer(EvmRequirement(), nano));

        Assert.That(selected, Is.SameAs(nano));
    }

    [Test]
    public async Task SelectPayment_ReturnsNull_WhenNoNanoOffer()
    {
        var selected = await Wallet(new FakeSender()).SelectPaymentAsync(Offer(EvmRequirement()));

        Assert.That(selected, Is.Null);
    }

    [TestCase("0.001")]
    [TestCase("-1")]
    [TestCase("1e27")]
    [TestCase("0")]
    [TestCase("")]
    public async Task SelectPayment_SkipsAmountsThatAreNotPositiveRawIntegers(string amount)
    {
        var selected = await Wallet(new FakeSender()).SelectPaymentAsync(Offer(NanoRequirement(amount)));

        Assert.That(selected, Is.Null);
    }

    [Test]
    public async Task SelectPayment_SkipsOtherSchemeAssetOrBadPayTo()
    {
        var upto = NanoRequirement();
        upto.Scheme = PaymentScheme.Upto;

        var selected = await Wallet(new FakeSender()).SelectPaymentAsync(Offer(
            upto,
            NanoRequirement(asset: "USDC"),
            NanoRequirement(payTo: "0x1111111111111111111111111111111111111111"),
            NanoRequirement(network: "nano:testnet")));

        Assert.That(selected, Is.Null);
    }

    [Test]
    public async Task SelectPayment_RespectsPerRequestAllowance()
    {
        var wallet = Wallet(new FakeSender());

        Assert.That(await wallet.SelectPaymentAsync(Offer(NanoRequirement((Price + 1).ToString()))), Is.Null);

        wallet.IgnoreAllowances = true;
        Assert.That(await wallet.SelectPaymentAsync(Offer(NanoRequirement((Price + 1).ToString()))), Is.Not.Null);
    }

    [Test]
    public async Task SelectPayment_WithoutAllowance_PaysNothing()
    {
        var wallet = new NanoWallet(Payer, new FakeSender());

        Assert.That(await wallet.SelectPaymentAsync(Offer(NanoRequirement())), Is.Null);
    }

    [Test]
    public async Task CreateHeader_SendsExactAmountToPayTo_AndCarriesBlockHash()
    {
        var sender = new FakeSender();
        var requirement = NanoRequirement();

        var header = await Wallet(sender).CreateHeaderAsync(requirement);

        Assert.That(sender.Sends, Has.Count.EqualTo(1));
        Assert.That(sender.Sends[0].Destination, Is.EqualTo(PayTo));
        Assert.That(sender.Sends[0].Amount, Is.EqualTo(Price));
        Assert.That(sender.Sends[0].Id, Is.Not.Empty);

        Assert.That(header.X402Version, Is.EqualTo(2));
        Assert.That(header.Accepted, Is.SameAs(requirement));
        Assert.That(header.Payload.BlockHash, Is.EqualTo(BlockHash));
        Assert.That(header.Payload.Authorization.From, Is.EqualTo(Payer));
        Assert.That(header.Payload.Authorization.To, Is.EqualTo(PayTo));
        Assert.That(header.Payload.Authorization.Value, Is.EqualTo("1000000000000000000000000000"));

        var parsed = PaymentPayloadHeader.FromHeader(header.ToBase64Header());
        Assert.That(parsed.Payload.BlockHash, Is.EqualTo(BlockHash));
        Assert.That(parsed.Accepted.Network, Is.EqualTo("nano:mainnet"));
    }

    [Test]
    public async Task CreateHeader_IsAcceptedAndSettledByNanoPaymentService()
    {
        var requirement = NanoRequirement();
        var header = await Wallet(new FakeSender()).CreateHeaderAsync(requirement);

        var rpc = new FakeRpc { Block = new NanoBlockInfo(Payer, "send", PayTo, Price, true) };
        var service = new NanoPaymentService(new[] { rpc }, new InMemoryNanoClaimStore());

        // The header goes over the wire as base64 JSON; verify what the server would parse.
        var received = PaymentPayloadHeader.FromHeader(header.ToBase64Header());

        var verify = await service.VerifyPayment(received, requirement);
        Assert.That(verify.IsValid, Is.True, verify.InvalidReason);
        Assert.That(verify.Payer, Is.EqualTo(Payer));

        var settle = await service.SettlePayment(received, requirement);
        Assert.That(settle.Success, Is.True, settle.ErrorReason);
        Assert.That(settle.Transaction, Is.EqualTo(BlockHash));
    }

    [Test]
    public async Task CreateHeader_BlockToOtherAccountOrAmount_IsRefusedByNanoPaymentService()
    {
        var requirement = NanoRequirement();
        var header = await Wallet(new FakeSender()).CreateHeaderAsync(requirement);
        var received = PaymentPayloadHeader.FromHeader(header.ToBase64Header());

        var wrongRecipient = new FakeRpc { Block = new NanoBlockInfo(Payer, "send", Payer, Price, true) };
        var verify = await new NanoPaymentService(new[] { wrongRecipient }, new InMemoryNanoClaimStore()).VerifyPayment(received, requirement);
        Assert.That(verify.IsValid, Is.False);
        Assert.That(verify.InvalidReason, Is.EqualTo(NanoErrorCodes.RecipientMismatch));

        var wrongAmount = new FakeRpc { Block = new NanoBlockInfo(Payer, "send", PayTo, Price - 1, true) };
        verify = await new NanoPaymentService(new[] { wrongAmount }, new InMemoryNanoClaimStore()).VerifyPayment(received, requirement);
        Assert.That(verify.IsValid, Is.False);
        Assert.That(verify.InvalidReason, Is.EqualTo(NanoErrorCodes.AmountMismatch));
    }

    [Test]
    public async Task CreateHeader_RefusesRequirementItCannotPay_WithoutSending()
    {
        var sender = new FakeSender();
        var wallet = Wallet(sender);

        await Assert.ThrowsAsync<ArgumentException>(() => wallet.CreateHeaderAsync(NanoRequirement(network: "eip155:8453")));
        await Assert.ThrowsAsync<ArgumentException>(() => wallet.CreateHeaderAsync(NanoRequirement(asset: "USDC")));
        await Assert.ThrowsAsync<ArgumentException>(() => wallet.CreateHeaderAsync(NanoRequirement(payTo: "nano_notanaccount")));
        await Assert.ThrowsAsync<ArgumentException>(() => wallet.CreateHeaderAsync(NanoRequirement(amount: "0.001")));
        Assert.That(sender.Sends, Is.Empty);
    }

    [Test]
    public async Task CreateHeader_LowersTotalAllowance_AndStopsWhenSpent()
    {
        var sender = new FakeSender();
        var wallet = Wallet(sender);

        for (var i = 0; i < 3; i++)
            await wallet.CreateHeaderAsync(NanoRequirement());

        Assert.That(wallet.Allowance!.TotalAllowanceRaw, Is.EqualTo(BigInteger.Zero));
        Assert.That(await wallet.SelectPaymentAsync(Offer(NanoRequirement())), Is.Null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => wallet.CreateHeaderAsync(NanoRequirement()));
        Assert.That(sender.Sends, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task CreateHeader_SenderReturnsNoBlockHash_Throws()
    {
        var wallet = Wallet(new FakeSender { Result = "not-a-hash" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => wallet.CreateHeaderAsync(NanoRequirement()));
    }

    [TestCase(PayTo, true)]
    [TestCase(Payer, true)]
    [TestCase("xrb_3t6k35gi95xu6tergt6p69ck76ogmitsa8mnijtpxm9fkcm736xtoncuohr3", true)]
    [TestCase("NANO_3T6K35GI95XU6TERGT6P69CK76OGMITSA8MNIJTPXM9FKCM736XTONCUOHR3", true)]
    [TestCase("nano_2t6k35gi95xu6tergt6p69ck76ogmitsa8mnijtpxm9fkcm736xtoncuohr3", false)]
    [TestCase("nano_3t6k35gi95xu6tergt6p69ck76ogmitsa8mnijtpxm9fkcm736xtoncuohr", false)]
    [TestCase("nano_3t6k35gi95xu6tergt6p69ck76ogmitsa8mnijtpxm9fkcm736xtoncuohrl", false)]
    [TestCase("0x1111111111111111111111111111111111111111", false)]
    [TestCase("", false)]
    public void IsAccount(string account, bool expected)
    {
        Assert.That(NanoWallet.IsAccount(account), Is.EqualTo(expected));
    }

    [Test]
    public void Constructor_RejectsInvalidAccount()
    {
        Assert.Throws<ArgumentException>(() => new NanoWallet("0x1111111111111111111111111111111111111111", new FakeSender()));
    }
}
