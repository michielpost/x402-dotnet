# x402-dotnet
x402 Payment Protocol implementation for .Net

Install the `x402` package from NuGet:
- [x402](https://nuget.org/packages/x402)


Features:
- Add a x402 compatible paywall to any URL
- Handle payment settlement using a remote server

## How to use?

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

