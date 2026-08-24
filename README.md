# x402-dotnet

![x402 payments enabled](https://github.com/michielpost/x402-dotnet/raw/master/images/x402-button-small.png)  
**x402 Payment Protocol implementation for .Net**  

Starting with version 2.0.0, this library targets x402 **v2**.  
All 0.x versions target x402 v1.


### x402 on the server
Install the `x402` packages from NuGet:
- [x402](https://nuget.org/packages/x402)
- [x402.Coinbase](https://nuget.org/packages/x402) to use the Coinbase facilitator

**Features:**
- Add an x402-compatible paywall to any URL  
- Easily use an attribute to handle payments for your API methods  
- Add URLs that require payment using the middleware  
- Support advanced scenarios by calling the `X402Handler` in your API controller
- Use endpoint filters to protect ASP.NET Core Minimal API endpoints  
- Handle payment settlement using any remote facilitator  
- Optionally use the Coinbase facilitator (with API key)
- Extensible AssetInfoProvider that fills in network and coin data based on the asset address
- Payment schemes: `exact`, `upto` (usage-based billing with settlement overrides) and `batch-settlement` (payment channels with a `ChannelManager`)
- Accept any ERC-20 token via Permit2 (`AssetTransferMethod`) with optional gas sponsorship extensions
- Discovery layer (Bazaar) support: expose `serviceName`, `tags` and `iconUrl` metadata on your endpoints, and query the facilitator's discovery API (list, merchant lookup, search)


### x402 enabled HttpClient
Install the `x402.Client.EVM` package from NuGet:
- [x402.Client.EVM](https://nuget.org/packages/x402.Client.EVM)

**Features:**
- Transparant access x402-protected resources
- Fully HttpClient compatible
- Pay using the embedded EVM compatible wallet (Ethereum / Base)
- Pay on the Casper Network with `x402.Client.Casper` (wCSPR settlement, Ed25519 and secp256k1 keys)
- Set allowances per request or globally
- X402.Client.ConsoleSample sample application included
- Blazor Sample project available

## How to use?

Register the x402 services and facilitator in `Program.cs`:
```cs
// Use the default HttpFacilitator
builder.Services.AddX402().WithHttpFacilitator(facilitatorUrl);
```

Use the `PaymentRequired` Attribute (easy to add to existing projects)
```cs
// Use the Payment Required Attribute
[HttpGet]
[Route("protected")]
[PaymentRequired("1000", "0x036CbD53842c5426634e7929541eC2318f3dCF7e", "0xYourAddressHere")]
public SampleResult Protected()
{
    return new SampleResult { Title = "Protected by PaymentRequired Attribute" };
}

```
Directly in an API Controller (for more control)
```cs
public ResourceController(X402HandlerV2 x402Handler)
{
    this.x402Handler = x402Handler;
}

[HttpGet]
[Route("dynamic")]
public async Task<SampleResult?> Dynamic(string amount)
{
    var x402Result = await x402Handler.HandleX402Async(this.HttpContext,
        new PaymentRequiredInfo()
        {
            Resource = new ResourceInfoBasic
            {
                Description = "This resource is protected dynamically",
            },
            Accepts = new List<PaymentRequirementsBasic>
            {
                new PaymentRequirementsBasic
                {
                    Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
                    Amount = amount,
                    PayTo = "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37",
                }
            }
        },);

    if (!x402Result.CanContinueRequest)
    {
        return null; // Response already written by HandleX402Async, so just exit
    }

    //Continue with the request
}
```


Or use the `PaymentMiddleware` to require payment for a list of URLs
```cs
// Add Middleware
var paymentOptions = new PaymentMiddlewareOptions
{
    PaymentRequirements = new Dictionary<string, PaymentRequirementsConfig>()
    {
        {  "/resource/middleware", new PaymentRequirementsConfig
            {
                Version = 2,
                PaymentRequirements = new PaymentRequiredInfo
                {
                    Resource = new ResourceInfoBasic
                    {
                         MimeType = "application/json",
                        Description = "Payment Required",
                    },
                    Accepts = new()
                    {
                        new PaymentRequirementsBasic {
                            Amount = "1000",
                            Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
                            PayTo = "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37", // Replace with your actual wallet address
                        }
                    },
                    Discoverable = true,
                }
            }
        }
    },
};

app.UsePaymentMiddleware(paymentOptions);

```

### Minimal APIs

You can also use `RequireX402Payment` endpoint filters to protect Minimal API routes:

```cs
using x402.Core.Enums;
using x402.Core.Models;
using x402.EndpointFilters;

// Free endpoint (no payment required)
app.MapGet("/api/free", () => "Free Resource");

// Protected with basic parameters
app.MapGet("/api/protected", () => new { Message = "Success!" })
    .RequireX402Payment(
        amount: "1000",
        asset: "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
        payTo: "0xYourAddressHere",
        description: "Protected Minimal API endpoint");

// Protected with PaymentRequiredInfo and output schema customization
app.MapPost("/api/send-msg", (SampleRequest req) =>
    new SampleResult { Title = $"Msg: {req.Value}" })
    .RequireX402Payment(
        new PaymentRequiredInfo
        {
            Resource = new ResourceInfoBasic { Description = "Send a message" },
            Accepts = new List<PaymentRequirementsBasic>
            {
                new()
                {
                    Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
                    Amount = "1000",
                    PayTo = "0xYourAddressHere",
                }
            },
            Discoverable = true
        },
        SettlementMode.Pessimistic,
        onSetOutputSchema: (context, reqs, schema) =>
        {
            schema.Input ??= new();
            schema.Input.BodyFields = new Dictionary<string, object>
            {
                {
                    nameof(SampleRequest.Value),
                    new FieldDefenition
                    {
                        Required = true,
                        Description = "Message to send",
                        Type = "string"
                    }
                }
            };

            return schema;
        });

// Dynamic amount based on the incoming request
app.MapGet("/api/dynamic", (HttpContext context, string amount) =>
{
    var x402Result = context.GetX402ResultV2();
    return new { Message = "Success!", Amount = amount, Payer = x402Result?.VerificationResponse?.Payer };
})
.RequireX402Payment(
    context =>
    {
        var amount = context.Request.Query["amount"].FirstOrDefault() ?? "1000";
        return new PaymentRequiredInfo
        {
            Resource = new ResourceInfoBasic { Description = "Dynamic endpoint" },
            Accepts = new List<PaymentRequirementsBasic>
            {
                new() { Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e", Amount = amount, PayTo = "0xYourAddressHere" }
            },
            Discoverable = true
        };
    },
    SettlementMode.Pessimistic,
    onSetOutputSchema: (context, reqs, schema) =>
    {
        schema.Input ??= new();
        schema.Input.QueryParams = new Dictionary<string, object>
        {
            {
                "amount",
                new FieldDefenition { Required = true, Description = "Amount to send", Type = "string" }
            }
        };
        return schema;
    });
```

## Payment Schemes: exact, upto and batch-settlement

Three payment schemes control how charges are calculated:
- **`exact`** (default) — the client pays the exact advertised price.
- **`upto`** — the client authorizes a maximum amount; the server settles only what was actually used (usage-based billing). EVM networks only.
- **`batch-settlement`** — requests are recorded as signed off-chain vouchers on a payment channel; a `ChannelManager` periodically batches vouchers into a single on-chain settlement. EVM networks only.

### upto

Set `Scheme = PaymentScheme.Upto` (the configured `Amount` becomes the maximum the client authorizes) and call `SetSettlementOverrides` in your handler to charge the actual usage:

```cs
app.MapGet("/api/generate", (HttpContext context) =>
{
    var actualUsage = 40000; // e.g. based on LLM token count

    // Settle only the actual usage — the client is never charged more than authorized
    context.SetSettlementOverrides(actualUsage.ToString());

    return new { Result = "Here is your generated text..." };
})
.RequireX402Payment(new PaymentRequiredInfo
{
    Resource = new ResourceInfoBasic { Description = "AI text generation — billed by token usage" },
    Accepts = new List<PaymentRequirementsBasic>
    {
        new()
        {
            Scheme = PaymentScheme.Upto,
            Amount = "100000", // Maximum the client authorizes (10 cents in 6-decimal USDC)
            Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
            PayTo = "0xYourAddressHere",
        }
    }
});
```

`SetSettlementOverrides` supports three formats:
- Raw atomic units — `"1000"` settles exactly 1,000 atomic units
- Percentage of the authorized amount — `"50%"` or `"33.33%"` (up to two decimals, floored)
- Dollar price — `"$0.05"`, converted to atomic units using the asset's decimals

The resolved amount must be `<=` the authorized maximum. If it resolves to `0`, no on-chain transaction occurs and the client is not charged.

### batch-settlement

Set `Scheme = PaymentScheme.BatchSettlement` and register a `ChannelManager` so requests are recorded as off-chain vouchers instead of settling per request:

```cs
builder.Services.AddX402().WithHttpFacilitator(facilitatorUrl);
builder.Services.AddX402ChannelManager();

var app = builder.Build();

// ChannelManager runs in the background: claims vouchers, settles them,
// and refunds idle channels on the configured intervals.
var channelManager = app.Services.GetRequiredService<ChannelManager>();
channelManager.Start(new ChannelManagerOptions
{
    ClaimIntervalSecs = 60,
    SettleIntervalSecs = 120,
    RefundIntervalSecs = 180,
    MaxClaimsPerBatch = 100,
    SelectRefundChannels = (channels, ctx) =>
        channels.Where(ch => ch.Balance > 0 && ctx.Now - ch.LastRequestTimestamp >= TimeSpan.FromMinutes(3)),
    OnClaim = r => Console.WriteLine($"Claimed {r.Vouchers} vouchers (tx: {r.Transaction})"),
    OnSettle = r => Console.WriteLine($"Settled {r.Amount} on {r.ChannelId}"),
    OnRefund = r => Console.WriteLine($"Refunded channel {r.Channel}"),
    OnError = e => Console.Error.WriteLine($"Settlement error: {e.Message}"),
});

app.Lifetime.ApplicationStopping.Register(() => channelManager.StopAsync(flush: true).GetAwaiter().GetResult());
```

Endpoint handlers can use the same `SetSettlementOverrides` formats as `upto` to bill a fraction of the authorized amount per request.

## Accept Any ERC-20 Token with Permit2 (Optional, EVM)

By default USDC is transferred via EIP-3009 (Transfer With Authorization). To accept any ERC-20 token, set the transfer method to Permit2 and optionally declare a gas sponsorship extension so the facilitator sponsors the buyer's one-time Permit2 approval:

```cs
using x402.Core.Extensions;

new PaymentRequiredInfo
{
    Resource = new ResourceInfoBasic { Description = "Protected" },
    Accepts = new List<PaymentRequirementsBasic>
    {
        new()
        {
            Amount = "1000",
            Asset = "0xYourTokenAddress",
            PayTo = "0xYourAddressHere",
            AssetTransferMethod = AssetTransferMethods.Permit2,
        }
    },
    // For tokens implementing EIP-2612 permit() (e.g. USDC):
    Extensions = new Dictionary<string, ExtensionData>()
        .With(GasSponsoringExtensions.DeclareEip2612GasSponsoringExtension()),
    // For generic ERC-20 tokens without EIP-2612, use
    // GasSponsoringExtensions.DeclareErc20ApprovalGasSponsoringExtension() instead.
};
```

Gas sponsorship requires facilitator support. Verify it first via the `/supported` endpoint:

```cs
var supported = await facilitatorClient.SupportedAsync();
if (supported.SupportsExtension(X402ExtensionKeys.Eip2612GasSponsoring))
{
    // safe to declare the extension on your routes
}
```

## Discovery layer (Bazaar)

Endpoints with `Discoverable = true` are indexed by the facilitator's discovery layer (e.g. the Coinbase Bazaar).
You can enrich your listing with provider-level metadata on the resource object of the 402 response:
`serviceName` (max 32 printable ASCII chars), `tags` (max 5 tags of 32 chars each) and `iconUrl`
(absolute http(s) URL, no IP literals or loopback hostnames). Facilitators silently drop fields that fail validation.

Using the attribute:
```cs
[PaymentRequired("1000", "0x036CbD53842c5426634e7929541eC2318f3dCF7e", "0xYourAddressHere",
    Discoverable = true,
    Description = "Premium API access for data analysis.",
    ServiceName = "Premium Data API",
    Tags = new[] { "data", "analytics" },
    IconUrl = "https://example.com/icon.png")]
```

Or on `ResourceInfoBasic` when using `PaymentRequiredInfo` (middleware, Minimal APIs or `X402HandlerV2`):
```cs
Resource = new ResourceInfoBasic
{
    Description = "Premium API access for data analysis.",
    ServiceName = "Premium Data API",
    Tags = new List<string> { "data", "analytics" },
    IconUrl = "https://example.com/icon.png",
}
```

Query the discovery API through any `IFacilitatorV2Client`:
```cs
// List all discovered resources (paginated)
var resources = await facilitatorClient.DiscoveryAsync(type: "http", limit: 100, offset: 0);

// Look up all resources of a merchant by payment address
var merchant = await facilitatorClient.DiscoveryMerchantAsync("0x742d35Cc6634C0532925a3b844Bc454e4438f44e");

// Search resources with a text query and filters
var search = await facilitatorClient.DiscoverySearchAsync(new DiscoverySearchRequest
{
    Query = "weather forecast",
    Network = "eip155:8453",
    MaxUsdPrice = "1.00",
});
```

## Coinbase Facilitator
To use the Coinbase Facilitator, install [x402.Coinbase](https://nuget.org/packages/x402.Coinbase)

```cs
// Add the Coinbase Config and Facilitator
builder.Services.AddX402().WithCoinbaseFacilitator(builder.Configuration);
```

Add to appsettings.json:
```json
 "CoinbaseOptions": {
   "ApiKeyId": "YOUR_COINBASE_API_KEY_ID",
   "ApiKeySecret": "YOUR_COINBASE_API_KEY_SECRET"
 }
```

## x402 HttpClient

```cs
// Fixed private key (32 bytes hex)
var wallet = new EVMWallet("0x0123454242abcdef0123456789abcdef0123456789abcdef0123456789abcdef", chainId) //84532UL = base-sepolia
{
    IgnoreAllowances = true
};

var handler = new PaymentRequiredV2Handler(new WalletProvider(wallet));

var client = new HttpClient(handler);
var response = await client.GetAsync("https://www.x402.org/protected");

Console.WriteLine($"Final: {(int)response.StatusCode} {response.ReasonPhrase}");
```

See `X402.Client.ConsoleSample` for a complete example.

## x402 HttpClient on Casper

Install the `x402.Client.Casper` package to pay on the Casper Network, where
payments settle in wCSPR (a CEP-18 token with 9 decimals) rather than USDC.

```cs
// Load a Casper key from a PEM file, the format the Casper client and the
// cspr.live wallet export
var wallet = CasperWallet.FromPem("secret_key.pem", CasperNetworks.Testnet)
{
    IgnoreAllowances = true
};

var handler = new PaymentRequiredV2Handler(new WalletProvider(wallet));

var client = new HttpClient(handler);
var response = await client.GetAsync("https://your-casper-resource/protected");
```

Casper uses CAIP-2 network identifiers, `casper:casper` for mainnet and
`casper:casper-test` for the testnet, and the asset is the CEP-18 contract
package hash of the settlement token:

```cs
new PaymentRequirementsBasic
{
    Amount = "1000000000", // 1 wCSPR, in motes
    Asset = "3d80df21ba4ee4d66a2a1f60c32570dd5685e4b279f6538162a5fd1314847c1e", // wCSPR on casper-test
    PayTo = "00...", // recipient account hash, prefixed with 00
    Extra = new PaymentRequirementsExtra { Name = "Wrapped CSPR", Version = "1" }
}
```

`extra.name` and `extra.version` are required on Casper: together with the
network id and the contract package hash they form the EIP-712 domain that the
authorization is signed against, so a payment missing them is rejected by the
facilitator rather than merely being incomplete.

Settle Casper payments with the Casper facilitator:

```cs
builder.Services.AddX402().WithHttpFacilitator("https://x402-facilitator.cspr.cloud");
```

Keys are signed locally: `CasperWallet` supports Ed25519 and secp256k1 keys, and
an overload taking a signing delegate lets you keep the key in a hardware wallet
or a key management service.

## x402-dotnet Facilitator
Explore the `x402.FacilitatorWeb` project for a dotnet based facilitator for EVM and Solana networks.


## How to test?
Follow these steps to test a x402 payment on the sample website hosted on Azure:
- Get some `USDC` tokens on the `base-sepolia` network from the [Coinbase Faucet](https://faucet.circle.com/)
- Use the x402 Debug Tool: https://proxy402.com/fetch
- Enter an API endpoint from the [test website](https://x402-dotnet.azurewebsites.net/), for example:
  - `https://x402-dotnet.azurewebsites.net/resource/middleware` (controller + middleware)
  - `https://x402-dotnet.azurewebsites.net/api/minimal/protected` (Minimal API)
- Connect your wallet
- Click Pay
- Payment will complete and show the result: `Protected by middleware`



## Public Facilitators
List of facilitators you can use:
- https://api.cdp.coinbase.com/platform/v2/x402/ (Coinbase, requires API key)
- https://facilitator.payai.network
- https://facilitator.mogami.tech/
- https://facilitator.daydreams.systems
- https://x402-facilitator.cspr.cloud (Casper, requires API key)

View more facilitators and their status on https://www.x402dev.com


## Development
There is a sample website and mock Settlement server included.  
- Start the Aspire project: `x402-dotnet.AppHost`
- Navigate to the sample website `https://localhost:7154/`
- Use `x402.SampleWeb.http` for sample web requests

## Contributions
Contributions are welcome. Fork this repository and send a pull request if you have something useful to add.


## Links
Useful tools when developing x402 solutions:
- More info about x402: https://www.x402.org
- Dev tools https://x402dev.com
- Test tool https://proxy402.com/fetch
- Specifications: https://github.com/coinbase/x402/blob/main/specs/x402-specification-v2.md

