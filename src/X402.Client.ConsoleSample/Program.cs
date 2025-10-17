using x402.Client;
using x402.Client.EVM;

Console.WriteLine("x402 client sample app");

var wallet = new EVMWallet("0x0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")
{
    IgnoreAllowances = true
};
var handler = new PaymentRequiredHandler(wallet);

var client = new HttpClient(handler);

var address = wallet.Account.Address;

var response = await client.GetAsync("https://www.x402.org/protected");

Console.WriteLine($"Final: {(int)response.StatusCode} {response.ReasonPhrase}");