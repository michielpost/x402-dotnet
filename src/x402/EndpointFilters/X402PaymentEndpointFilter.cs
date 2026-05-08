using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using x402.Core.Enums;
using x402.Core.Interfaces;
using x402.Core.Models;
using x402.Core.Models.Facilitator;
using x402.Core.Models.v2;

namespace x402.EndpointFilters;

/// <summary>
/// Endpoint filter that enforces x402 payment requirements on Minimal API endpoints.
/// </summary>
public class X402PaymentEndpointFilter : IEndpointFilter
{
    private readonly PaymentRequiredInfo? paymentRequiredInfo;
    private readonly Func<HttpContext, PaymentRequiredInfo>? paymentRequiredInfoFactory;
    private readonly SettlementMode settlementMode;
    private readonly Func<HttpContext, PaymentRequirements, OutputSchema, OutputSchema>? onSetOutputSchema;
    private readonly Func<HttpContext, SettlementResponse?, Exception?, Task>? onSettlement;

    /// <summary>
    /// Creates a new instance of the filter using <see cref="PaymentRequiredInfo"/>.
    /// </summary>
    public X402PaymentEndpointFilter(
        PaymentRequiredInfo paymentRequiredInfo,
        SettlementMode settlementMode = SettlementMode.Pessimistic,
        Func<HttpContext, SettlementResponse?, Exception?, Task>? onSettlement = null,
        Func<HttpContext, PaymentRequirements, OutputSchema, OutputSchema>? onSetOutputSchema = null)
    {
        this.paymentRequiredInfo = paymentRequiredInfo;
        this.settlementMode = settlementMode;
        this.onSettlement = onSettlement;
        this.onSetOutputSchema = onSetOutputSchema;
    }

    /// <summary>
    /// Creates a new instance of the filter using a factory that builds <see cref="PaymentRequiredInfo"/> from the <see cref="HttpContext"/>.
    /// </summary>
    public X402PaymentEndpointFilter(
        Func<HttpContext, PaymentRequiredInfo> paymentRequiredInfoFactory,
        SettlementMode settlementMode = SettlementMode.Pessimistic,
        Func<HttpContext, SettlementResponse?, Exception?, Task>? onSettlement = null,
        Func<HttpContext, PaymentRequirements, OutputSchema, OutputSchema>? onSetOutputSchema = null)
    {
        this.paymentRequiredInfoFactory = paymentRequiredInfoFactory;
        this.settlementMode = settlementMode;
        this.onSettlement = onSettlement;
        this.onSetOutputSchema = onSetOutputSchema;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var x402Handler = context.HttpContext.RequestServices.GetRequiredService<X402HandlerV2>();

        var info = paymentRequiredInfoFactory != null
            ? paymentRequiredInfoFactory(context.HttpContext)
            : paymentRequiredInfo!;

        var x402Result = await x402Handler.HandleX402Async(
            info,
            settlementMode,
            onSettlement,
            onSetOutputSchema);

        if (!x402Result.CanContinueRequest)
        {
            return Results.Empty;
        }

        return await next(context);
    }
}
