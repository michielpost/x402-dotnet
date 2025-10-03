using x402;
using x402.Facilitator;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

var httpClientFactory = app.Services.GetRequiredService<IHttpClientFactory>();

var paymentOptions = new x402.Models.PaymentMiddlewareOptions
{
    Facilitator = new HttpFacilitatorClient(httpClientFactory, "https://localhost:7141"),
    DefaultPayToAddress = "0xYourWalletAddressHere", // Replace with your actual wallet address
    PaymentRequirements = new Dictionary<string, x402.Models.PaymentRequirementsConfig>()
        {
            {  "/", new x402.Models.PaymentRequirementsConfig
                {
                    Scheme = x402.Enums.PaymentScheme.Exact,
                    PayTo = "0xYourWalletAddressHere", // Replace with your actual wallet address
                    MaxAmountRequired = 1000000,
                    Asset = "USDC",
                    MimeType = "application/json"
                }
            }
        },
};

app.UsePaymentMiddleware(paymentOptions);

app.MapControllers();

app.Run();
