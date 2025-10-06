# x402-dotnet
x402 Payment Protocol implementation for .Net

Install the `x402` package from NuGet:
- [x402](https://nuget.org/packages/x402)


Features:
- Add a x402 compatible paywall to any URL
- Handle payment settlement using a remote server

## How to use?

Setup the Facilitator in Program.cs
```cs
// Add the facilitator in Program.cs
builder.Services.AddHttpClient<IFacilitatorClient, HttpFacilitatorClient>(client =>
{
    client.BaseAddress = new Uri("https://localhost:7141"); // Address of your facilitator
});
```

// Use the MVC Attribute
```cs
// Use the Payment Required Attribute
 [HttpGet]
 [Route("protected")]
 [PaymentRequired("1", "0x00..Asset..Address", "0xYourWalletAddressHere", "base-sepolia")]
 public IActionResult Protected()
 {
     return Content("Protected");
 }

```

Or use the Middleware to require payment for a list of URLs
```cs
// Add Middleware
var facilitator = app.Services.GetRequiredService<IFacilitatorClient>();
var paymentOptions = new x402.Models.PaymentMiddlewareOptions
{
    Facilitator = facilitator,
    DefaultPayToAddress = "0xYourWalletAddressHere", // Replace with your actual wallet address
    DefaultNetwork = "base-sepolia",
    PaymentRequirements = new Dictionary<string, x402.Models.PaymentRequirementsConfig>()
        {
            {  "/url-to-pay-for", new x402.Models.PaymentRequirementsConfig
                {
                    Scheme = x402.Enums.PaymentScheme.Exact,
                    MaxAmountRequired = 1000000,
                    Asset = "0x......", // Contract address of asset
                    MimeType = "application/json"
                }
            }
        },
};

app.UsePaymentMiddleware(paymentOptions);

```

## Coinbase
To use the Coinbase Facilitator, install [x402.Coinbase](https://nuget.org/packages/x402.Coinbase)

```cs
// Add the Coinbase Config and Facilitator
builder.Services.Configure<CoinbaseOptions>(builder.Configuration.GetSection(nameof(CoinbaseOptions)));
builder.Services.AddHttpClient<IFacilitatorClient, CoinbaseFacilitatorClient>();
```

Add to appsettings.json:
```json
 "CoinbaseOptions": {
   "ApiKeyId": "YOUR_COINBASE_API_KEY_ID",
   "ApiKeySecret": "YOUR_COINBASE_API_KEY_SECRET"
 }
```


## Faciliators
List of facilitators you can use:
- https://api.cdp.coinbase.com/platform/v2/x402/ (Coinbase, requires API key)
- https://facilitator.payai.network
- https://facilitator.mogami.tech/
- https://facilitator.mcpay.tech (Proxy Faciliator)


## Development
There is a sample website and mock Settlement server included.  
- Start the Aspire project: `x402-dotnet.AppHost`
- Navigate to the sample website `https://localhost:7154/`
- Use the `x402.SampleWeb.http` for sample web requests


## Tools
Useful tools when developing x402 solutions:
- https://proxy402.com/fetch

