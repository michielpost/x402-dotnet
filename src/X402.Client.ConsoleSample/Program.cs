using Microsoft.Extensions.Configuration;
using System.Text.Json;
using x402.Client;
using x402.Client.EVM;

Console.WriteLine("Welcome to the x402 client sample app");

// Load configuration
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

// Read values from configuration
var privateKey = config["EVMWallet:PrivateKey"];
var networkIdString = config["EVMWallet:NetworkId"];

if (string.IsNullOrWhiteSpace(privateKey) || string.IsNullOrWhiteSpace(networkIdString))
{
    Console.WriteLine("Missing EVMWallet configuration. Please set EVMWallet:PrivateKey and EVMWallet:NetworkId.");
    return;
}

if (!ulong.TryParse(networkIdString, out var networkId))
{
    Console.WriteLine("Invalid NetworkId value in configuration.");
    return;
}

var wallet = new EVMWallet(privateKey, networkId)
{
    IgnoreAllowances = true
};
var handler = new PaymentRequiredHandler(wallet);

handler.PaymentRequiredReceived += (_, e) =>
{
    Console.WriteLine($"402 received for {e.Request.RequestUri}");

    //Console.WriteLine($"Payment Required: {JsonSerializer.Serialize(e.PaymentRequiredResponse)}");

};

handler.PaymentSelected += (_, e) =>
{
    Console.WriteLine();
    Console.WriteLine($"Selected payment requirement: {JsonSerializer.Serialize(e.PaymentRequirements)}");
    Console.WriteLine();
    Console.WriteLine($"Responding with payment header: {JsonSerializer.Serialize(e.PaymentHeader)}");
    Console.WriteLine();

};

handler.PaymentRetrying += (_, e) =>
{
    Console.WriteLine($"Retrying {e.Request.RequestUri} with payment header, attempt #{e.Attempt}");
};

var client = new HttpClient(handler);

var address = wallet.Account.Address;

Console.WriteLine($"Using wallet address: {address}");

var defaultUrl = "https://www.x402.org/protected";
Console.Write($"Enter a URL or press Enter to use default ({defaultUrl}): ");
var input = Console.ReadLine();
var urlToUse = string.IsNullOrWhiteSpace(input) ? defaultUrl : input!.Trim();

if (!Uri.TryCreate(urlToUse, UriKind.Absolute, out var parsed) ||
    (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
{
    Console.WriteLine("Invalid URL. Please enter a valid http(s) URL.");
    return;
}

var response = await client.GetAsync(urlToUse);

Console.WriteLine($"Final: {(int)response.StatusCode} {response.ReasonPhrase}");

var header = response.ReadSettlementResponseHeader();
if (header != null)
{
    Console.WriteLine("Settlement Response Header:");
    Console.WriteLine(JsonSerializer.Serialize(header, new JsonSerializerOptions { WriteIndented = true }));
}
else
{
    Console.WriteLine("No Settlement Response Header found.");
}

Console.Write("Do you want to see the full response content? (Y/n): ");
var showContent = Console.ReadLine();
if (string.IsNullOrWhiteSpace(showContent) || showContent.Trim().Equals("y", StringComparison.OrdinalIgnoreCase))
{
    var content = await response.Content.ReadAsStringAsync();
    Console.WriteLine("Response content:");
    Console.WriteLine(content);
}

Console.WriteLine("Press Enter to close");
Console.ReadLine();
