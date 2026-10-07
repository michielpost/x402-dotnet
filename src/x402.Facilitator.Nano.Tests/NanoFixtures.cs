using System.Numerics;
using System.Text.Json;
using x402.Core.Enums;
using x402.Core.Models.v2;

namespace x402.Facilitator.Nano.Tests;

/// <summary>
/// Fake ledger data. Nothing here is a key or a seed: the block hashes are a
/// repeated hex digit and the accounts are public, well-known addresses.
/// </summary>
internal static class NanoFixtures
{
    // Not real blocks: one hex digit repeated 64 times.
    public static readonly string BlockHash = new('A', 64);
    public static readonly string OtherBlockHash = new('B', 64);

    // Public accounts: the burn account and the genesis account.
    public const string PayTo = "nano_1111111111111111111111111111111111111111111111111111hifc8npp";
    public const string Payer = "nano_3t6k35gi95xu6tergt6p69ck76ogmitsa8mnijtpxm9fkcm736xtoncuohr3";
    public const string Stranger = "nano_1stranger1111111111111111111111111111111111111111111111fixture";

    // 0.001 XNO, derived from 1 XNO = 10^30 raw rather than written out.
    public static readonly BigInteger Price = NanoNetworks.RawPerXno / 1000;

    public static PaymentRequirements Requirements(BigInteger? amount = null, string payTo = PayTo) => new()
    {
        Scheme = PaymentScheme.Exact,
        Network = NanoNetworks.Mainnet,
        Amount = (amount ?? Price).ToString(),
        Asset = NanoNetworks.AssetXno,
        PayTo = payTo
    };

    public static PaymentPayloadHeader Header(string? blockHash, string from = Payer, PaymentRequirements? accepted = null)
    {
        accepted ??= Requirements();
        return new PaymentPayloadHeader
        {
            X402Version = 2,
            Accepted = accepted,
            Payload = new Payload
            {
                BlockHash = blockHash,
                Authorization = new Authorization
                {
                    From = from,
                    To = accepted.PayTo,
                    Value = accepted.Amount
                }
            }
        };
    }

    /// <summary>
    /// A block_info response in the shape a node gives for a state block with json_block=true.
    /// </summary>
    public static string BlockInfoJson(
        BigInteger? amount = null,
        string destination = PayTo,
        string account = Payer,
        string subtype = "send",
        string confirmed = "true")
    {
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["block_account"] = account,
            ["amount"] = (amount ?? Price).ToString(),
            ["balance"] = "0",
            ["height"] = "58",
            ["local_timestamp"] = "0",
            ["successor"] = new string('0', 64),
            ["confirmed"] = confirmed,
            ["contents"] = new Dictionary<string, string>
            {
                ["type"] = "state",
                ["account"] = account,
                ["previous"] = new string('C', 64),
                ["representative"] = account,
                ["balance"] = "0",
                ["link"] = new string('0', 64),
                ["link_as_account"] = destination,
                ["signature"] = new string('0', 128),
                ["work"] = new string('0', 16)
            },
            ["subtype"] = subtype
        });
    }
}

/// <summary>
/// A node that answers from recorded block_info responses, parsed by the real parser.
/// </summary>
internal class FakeNanoRpcClient : INanoRpcClient
{
    private readonly Dictionary<string, string> _responses = new();

    private int _calls;

    public int Calls => _calls;
    public Exception? Throw { get; set; }

    public FakeNanoRpcClient With(string blockHash, string blockInfoJson)
    {
        _responses[blockHash] = blockInfoJson;
        return this;
    }

    public Task<NanoBlockInfo?> GetBlockInfoAsync(string blockHash, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);
        if (Throw != null)
            throw Throw;

        var json = _responses.TryGetValue(blockHash, out var recorded) ? recorded : "{\"error\":\"Block not found\"}";
        return Task.FromResult(NanoRpcClient.ParseBlockInfo(json));
    }
}
