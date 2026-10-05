using System.Net;
using System.Text;

namespace x402.Facilitator.Nano.Tests;

[TestFixture]
public class NanoRpcClientTests
{
    private const string Endpoint = "https://nano-node.invalid/rpc";

    private class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public HttpRequestMessage? Request { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    [Test]
    public async Task GetBlockInfo_PostsBlockInfoAction_AndParsesAStateSendBlock()
    {
        var handler = new StubHandler(HttpStatusCode.OK, NanoFixtures.BlockInfoJson());
        var client = new NanoRpcClient(new HttpClient(handler), Endpoint);

        var block = await client.GetBlockInfoAsync(NanoFixtures.BlockHash);

        Assert.That(handler.Request!.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(handler.Request.RequestUri, Is.EqualTo(new Uri(Endpoint)));
        Assert.That(handler.RequestBody, Does.Contain("\"action\":\"block_info\""));
        Assert.That(handler.RequestBody, Does.Contain("\"json_block\":\"true\""));
        Assert.That(handler.RequestBody, Does.Contain($"\"hash\":\"{NanoFixtures.BlockHash}\""));

        Assert.That(block, Is.Not.Null);
        Assert.That(block!.Account, Is.EqualTo(NanoFixtures.Payer));
        Assert.That(block.Subtype, Is.EqualTo("send"));
        Assert.That(block.Destination, Is.EqualTo(NanoFixtures.PayTo));
        Assert.That(block.AmountRaw, Is.EqualTo(NanoFixtures.Price));
        Assert.That(block.Confirmed, Is.True);
    }

    [Test]
    public async Task GetBlockInfo_BlockNotFound_ReturnsNull()
    {
        var client = new NanoRpcClient(new HttpClient(new StubHandler(HttpStatusCode.OK, "{\"error\":\"Block not found\"}")), Endpoint);

        Assert.That(await client.GetBlockInfoAsync(NanoFixtures.BlockHash), Is.Null);
    }

    [Test]
    public async Task GetBlockInfo_OtherRpcError_Throws()
    {
        var client = new NanoRpcClient(new HttpClient(new StubHandler(HttpStatusCode.OK, "{\"error\":\"Invalid block hash\"}")), Endpoint);

        await Assert.ThrowsAsync<NanoRpcException>(() => client.GetBlockInfoAsync(NanoFixtures.BlockHash));
    }

    [Test]
    public async Task GetBlockInfo_HttpError_Throws()
    {
        var client = new NanoRpcClient(new HttpClient(new StubHandler(HttpStatusCode.BadGateway, "upstream down")), Endpoint);

        await Assert.ThrowsAsync<NanoRpcException>(() => client.GetBlockInfoAsync(NanoFixtures.BlockHash));
    }

    [TestCase("not json")]
    [TestCase("[]")]
    public void ParseBlockInfo_UnusableAnswer_Throws(string body)
    {
        Assert.Throws<NanoRpcException>(() => NanoRpcClient.ParseBlockInfo(body));
    }

    [Test]
    public void ParseBlockInfo_LegacySendBlock_ReadsTypeAndDestinationFromContents()
    {
        var json = $$"""
            {
              "block_account": "{{NanoFixtures.Payer}}",
              "amount": "{{NanoFixtures.Price}}",
              "confirmed": "true",
              "contents": { "type": "send", "destination": "{{NanoFixtures.PayTo}}", "balance": "0" }
            }
            """;

        var block = NanoRpcClient.ParseBlockInfo(json);

        Assert.That(block!.Subtype, Is.EqualTo("send"));
        Assert.That(block.Destination, Is.EqualTo(NanoFixtures.PayTo));
        Assert.That(block.AmountRaw, Is.EqualTo(NanoFixtures.Price));
    }

    [Test]
    public void ParseBlockInfo_StateBlockWithoutSubtype_HasNoSubtype()
    {
        var json = $$"""
            {
              "block_account": "{{NanoFixtures.Payer}}",
              "amount": "{{NanoFixtures.Price}}",
              "confirmed": "true",
              "contents": { "type": "state", "link_as_account": "{{NanoFixtures.PayTo}}" }
            }
            """;

        Assert.That(NanoRpcClient.ParseBlockInfo(json)!.Subtype, Is.Null);
    }

    [TestCase("\"1.5\"")]
    [TestCase("\"-5\"")]
    [TestCase("\"1e30\"")]
    [TestCase("1000")] // a JSON number could already have lost precision
    public void ParseBlockInfo_AmountThatIsNotAnIntegerString_HasNoAmount(string amount)
    {
        var json = $$"""{ "amount": {{amount}}, "confirmed": "true", "subtype": "send" }""";

        Assert.That(NanoRpcClient.ParseBlockInfo(json)!.AmountRaw, Is.Null);
    }

    [Test]
    public void ParseBlockInfo_MissingConfirmedField_IsNotConfirmed()
    {
        Assert.That(NanoRpcClient.ParseBlockInfo("""{ "subtype": "send", "amount": "1" }""")!.Confirmed, Is.False);
    }
}
