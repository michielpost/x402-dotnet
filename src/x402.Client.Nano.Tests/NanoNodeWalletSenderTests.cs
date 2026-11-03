using System.Net;
using System.Numerics;
using System.Text;
using System.Text.Json;
using x402.Client.Nano;

namespace x402.Client.Nano.Tests;

public class NanoNodeWalletSenderTests
{
    private const string PayTo = "nano_1111111111111111111111111111111111111111111111111111hifc8npp";
    private const string Payer = "nano_3t6k35gi95xu6tergt6p69ck76ogmitsa8mnijtpxm9fkcm736xtoncuohr3";

    // Not a real wallet id or block: repeated hex digits.
    private static readonly string WalletId = new('0', 64);
    private static readonly string BlockHash = new('A', 64);

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<string> responses;
        public readonly List<Dictionary<string, string>> Requests = new();

        public ScriptedHandler(params string[] responses) => this.responses = new Queue<string>(responses);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Add(JsonSerializer.Deserialize<Dictionary<string, string>>(body)!);

            var json = responses.Count > 1 ? responses.Dequeue() : responses.Peek();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private static NanoNodeWalletSender Sender(ScriptedHandler handler) =>
        new(new HttpClient(handler), "http://127.0.0.1:7076", WalletId, Payer)
        {
            PollInterval = TimeSpan.FromMilliseconds(1),
            ConfirmationTimeout = TimeSpan.FromSeconds(5)
        };

    [Test]
    public async Task SendAsync_PostsSend_ThenWaitsForConfirmation()
    {
        var handler = new ScriptedHandler(
            $$"""{"block":"{{BlockHash}}"}""",
            """{"confirmed":"false"}""",
            """{"confirmed":"true"}""");
        var amount = BigInteger.Pow(10, 27);

        var hash = await Sender(handler).SendAsync(PayTo, amount, "attempt-1");

        Assert.That(hash, Is.EqualTo(BlockHash));
        Assert.That(handler.Requests, Has.Count.EqualTo(3));

        var send = handler.Requests[0];
        Assert.That(send["action"], Is.EqualTo("send"));
        Assert.That(send["wallet"], Is.EqualTo(WalletId));
        Assert.That(send["source"], Is.EqualTo(Payer));
        Assert.That(send["destination"], Is.EqualTo(PayTo));
        Assert.That(send["amount"], Is.EqualTo("1000000000000000000000000000"));
        Assert.That(send["id"], Is.EqualTo("attempt-1"));

        Assert.That(handler.Requests[1]["action"], Is.EqualTo("block_info"));
        Assert.That(handler.Requests[1]["hash"], Is.EqualTo(BlockHash));
    }

    [Test]
    public async Task SendAsync_BlockNotFoundYet_KeepsPolling()
    {
        // A node can answer "Block not found" for a moment right after "send" returns.
        var handler = new ScriptedHandler(
            $$"""{"block":"{{BlockHash}}"}""",
            """{"error":"Block not found"}""",
            """{"confirmed":"true"}""");

        var hash = await Sender(handler).SendAsync(PayTo, BigInteger.One, "attempt-1");

        Assert.That(hash, Is.EqualTo(BlockHash));
        Assert.That(handler.Requests, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task SendAsync_ConfirmedAsJsonBoolean_IsConfirmed()
    {
        var handler = new ScriptedHandler(
            $$"""{"block":"{{BlockHash}}"}""",
            """{"confirmed":true}""");

        var hash = await Sender(handler).SendAsync(PayTo, BigInteger.One, "attempt-1");

        Assert.That(hash, Is.EqualTo(BlockHash));
    }

    [Test]
    public async Task SendAsync_UppercaseAccounts_AreSentLowercase()
    {
        var handler = new ScriptedHandler($$"""{"block":"{{BlockHash}}"}""", """{"confirmed":"true"}""");
        var sender = new NanoNodeWalletSender(new HttpClient(handler), "http://127.0.0.1:7076", WalletId, Payer.ToUpperInvariant())
        {
            PollInterval = TimeSpan.FromMilliseconds(1)
        };

        await sender.SendAsync(PayTo.ToUpperInvariant(), BigInteger.One, "attempt-1");

        Assert.That(handler.Requests[0]["source"], Is.EqualTo(Payer));
        Assert.That(handler.Requests[0]["destination"], Is.EqualTo(PayTo));
    }

    [Test]
    public async Task SendAsync_RpcError_Throws()
    {
        var handler = new ScriptedHandler("""{"error":"Insufficient balance"}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sender(handler).SendAsync(PayTo, BigInteger.One, "attempt-1"));
        Assert.That(ex!.Message, Does.Contain("Insufficient balance"));
    }

    [Test]
    public async Task SendAsync_NeverConfirmed_TimesOut()
    {
        var handler = new ScriptedHandler($$"""{"block":"{{BlockHash}}"}""", """{"confirmed":"false"}""");
        var sender = Sender(handler);
        sender.ConfirmationTimeout = TimeSpan.FromMilliseconds(50);

        await Assert.ThrowsAsync<TimeoutException>(() => sender.SendAsync(PayTo, BigInteger.One, "attempt-1"));
    }

    [Test]
    public async Task SendAsync_RejectsBadInput_WithoutCallingTheNode()
    {
        var handler = new ScriptedHandler("{}");
        var sender = Sender(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => sender.SendAsync("0x1111111111111111111111111111111111111111", BigInteger.One, "attempt-1"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sender.SendAsync(PayTo, BigInteger.Zero, "attempt-1"));
        Assert.That(handler.Requests, Is.Empty);
    }
}
