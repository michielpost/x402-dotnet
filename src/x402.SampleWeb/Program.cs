using x402;
using x402.Coinbase;
using x402.Coinbase.Models;
using x402.Core.Enums;
using x402.Facilitator;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddRazorPages();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "x402-dotnet sample", Version = "v1" });
});

// Add CORS to allow testing from https://proxy402.com/fetch
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .AllowAnyOrigin()   // allow any domain
            .AllowAnyMethod()   // allow GET, POST, PUT, DELETE, etc.
            .AllowAnyHeader();  // allow all headers
    });
});


var facilitatorUrl = builder.Configuration["FacilitatorUrl"];
if (!string.IsNullOrEmpty(facilitatorUrl))
{
    builder.Services.AddHttpClient<IFacilitatorClient, HttpFacilitatorClient>(client =>
    {
        client.BaseAddress = new Uri(facilitatorUrl);
    });
}
else
{
    // Coinbase facilitator client
    builder.Services.Configure<CoinbaseOptions>(builder.Configuration.GetSection(nameof(CoinbaseOptions)));
    builder.Services.AddHttpClient<IFacilitatorClient, CoinbaseFacilitatorClient>();
}

var app = builder.Build();

// Use CORS before endpoints
app.UseCors("AllowAll");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

var facilitator = app.Services.GetRequiredService<IFacilitatorClient>();
var paymentOptions = new x402.Models.PaymentMiddlewareOptions
{
    DefaultPayToAddress = "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37", // Replace with your actual wallet address
    DefaultNetwork = "base-sepolia",
    PaymentRequirements = new Dictionary<string, x402.Models.PaymentRequirementsConfig>()
        {
            {  "/resource/middleware", new x402.Models.PaymentRequirementsConfig
                {
                    Scheme = PaymentScheme.Exact,
                    MaxAmountRequired = "1000",
                    Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
                    MimeType = "application/json",
                    Description = "Payment Required",
                    Discoverable = true
                }
            }
        },
};

app.UsePaymentMiddleware(paymentOptions);

app.UseStaticFiles();
app.MapRazorPages();
app.MapControllers();
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "x402-dotnet sample");
});

app.Run();
