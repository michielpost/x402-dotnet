using x402;
using x402.Coinbase;
using x402.Core.Enums;
using x402.Core.Models;
using x402.Core.Models.v2;
using x402.EndpointFilters;
using x402.Models;
using x402.SampleWeb.Models;

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
    builder.Services.AddX402().WithHttpFacilitator(facilitatorUrl);

}
else
{
    builder.Services.AddX402().WithCoinbaseFacilitator(builder.Configuration);
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
        },
        {  "/resourcev2/middleware", new PaymentRequirementsConfig
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
    }
};

app.UsePaymentMiddleware(paymentOptions);

app.UseStaticFiles();
app.MapRazorPages();
app.MapControllers();

// Minimal API endpoints with x402 payment protection

// Free endpoint (no payment required)
app.MapGet("/api/minimal/free", () => new SampleResult { Title = "Free Minimal API Resource" });

// Protected endpoint with basic parameters
app.MapGet("/api/minimal/protected", () => new SampleResult { Title = "Success! Protected by x402 Minimal API Filter" })
    .RequireX402Payment(
        amount: "1000",
        asset: "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
        payTo: "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37",
        description: "Minimal API protected endpoint",
        settlementMode: SettlementMode.Pessimistic,
        discoverable: true);

// Protected endpoint with PaymentRequiredInfo and output schema customization
app.MapPost("/api/minimal/send-msg", (SampleRequest req) =>
    new SampleResult { Title = $"Success! Msg: {req.Value}, paid by: {req.Value}" })
    .RequireX402Payment(
        new PaymentRequiredInfo
        {
            Resource = new ResourceInfoBasic
            {
                Description = "Send a message via Minimal API",
            },
            Accepts = new List<PaymentRequirementsBasic>
            {
                new()
                {
                    Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
                    Amount = "1000",
                    PayTo = "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37",
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

// Dynamic protected endpoint with amount from query string
app.MapGet("/api/minimal/dynamic", (HttpContext context, string amount) =>
{
    var x402Result = context.GetX402ResultV2();
    var txInfo = x402Result?.SettlementResponse?.Transaction;
    var payer = x402Result?.VerificationResponse?.Payer;
    return new SampleResult { Title = $"Success! Dynamic protected for {amount}, paid by: {payer}. Tx: {txInfo}" };
})
.RequireX402Payment(
    new PaymentRequiredInfo
    {
        Resource = new ResourceInfoBasic
        {
            Description = "Dynamic payment Minimal API",
        },
        Accepts = new List<PaymentRequirementsBasic>
        {
            new()
            {
                Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
                Amount = "1000",
                PayTo = "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37",
            }
        },
        Discoverable = true
    },
    SettlementMode.Pessimistic,
    onSetOutputSchema: (context, reqs, schema) =>
    {
        schema.Input ??= new();
        schema.Input.QueryParams = new Dictionary<string, object>
        {
            {
                "amount",
                new FieldDefenition
                {
                    Required = true,
                    Description = "Amount to send",
                    Type = "string"
                }
            }
        };
        return schema;
    });

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "x402-dotnet sample");
});

app.Run();
