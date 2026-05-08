using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using x402.Core.Enums;
using x402.Core.Models;
using x402.Core.Models.Facilitator;
using x402.Core.Models.v2;

namespace x402.EndpointFilters;

/// <summary>
/// Extension methods for adding x402 payment requirements to Minimal API endpoints.
/// </summary>
public static class X402PaymentEndpointFilterBuilderExtensions
{
    /// <summary>
    /// Requires x402 payment on this endpoint using <see cref="PaymentRequiredInfo"/>.
    /// </summary>
    public static TBuilder RequireX402Payment<TBuilder>(
        this TBuilder builder,
        PaymentRequiredInfo paymentRequiredInfo,
        SettlementMode settlementMode = SettlementMode.Pessimistic)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(new X402PaymentEndpointFilter(paymentRequiredInfo, settlementMode));
        return builder;
    }

    /// <summary>
    /// Requires x402 payment on this endpoint using <see cref="PaymentRequiredInfo"/> with output schema customization.
    /// </summary>
    public static TBuilder RequireX402Payment<TBuilder>(
        this TBuilder builder,
        PaymentRequiredInfo paymentRequiredInfo,
        SettlementMode settlementMode,
        Func<HttpContext, PaymentRequirements, OutputSchema, OutputSchema> onSetOutputSchema)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(new X402PaymentEndpointFilter(paymentRequiredInfo, settlementMode, onSetOutputSchema: onSetOutputSchema));
        return builder;
    }

    /// <summary>
    /// Requires x402 payment on this endpoint using <see cref="PaymentRequiredInfo"/> with settlement callback.
    /// </summary>
    public static TBuilder RequireX402Payment<TBuilder>(
        this TBuilder builder,
        PaymentRequiredInfo paymentRequiredInfo,
        SettlementMode settlementMode,
        Func<HttpContext, SettlementResponse?, Exception?, Task> onSettlement)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(new X402PaymentEndpointFilter(paymentRequiredInfo, settlementMode, onSettlement));
        return builder;
    }

    /// <summary>
    /// Requires x402 payment on this endpoint using <see cref="PaymentRequiredInfo"/> with full customization.
    /// </summary>
    public static TBuilder RequireX402Payment<TBuilder>(
        this TBuilder builder,
        PaymentRequiredInfo paymentRequiredInfo,
        SettlementMode settlementMode,
        Func<HttpContext, SettlementResponse?, Exception?, Task> onSettlement,
        Func<HttpContext, PaymentRequirements, OutputSchema, OutputSchema> onSetOutputSchema)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(new X402PaymentEndpointFilter(paymentRequiredInfo, settlementMode, onSettlement, onSetOutputSchema));
        return builder;
    }

    /// <summary>
    /// Requires x402 payment on this endpoint using basic payment parameters.
    /// </summary>
    public static TBuilder RequireX402Payment<TBuilder>(
        this TBuilder builder,
        string amount,
        string asset,
        string payTo,
        string description = "",
        SettlementMode settlementMode = SettlementMode.Pessimistic,
        bool discoverable = true)
        where TBuilder : IEndpointConventionBuilder
    {
        var paymentRequiredInfo = new PaymentRequiredInfo
        {
            Resource = new ResourceInfoBasic
            {
                Description = description,
            },
            Accepts = new List<PaymentRequirementsBasic>
            {
                new()
                {
                    Amount = amount,
                    Asset = asset,
                    PayTo = payTo,
                }
            },
            Discoverable = discoverable
        };

        builder.AddEndpointFilter(new X402PaymentEndpointFilter(paymentRequiredInfo, settlementMode));
        return builder;
    }

    /// <summary>
    /// Requires x402 payment on this endpoint using a factory that builds <see cref="PaymentRequiredInfo"/> from the <see cref="HttpContext"/>.
    /// </summary>
    public static TBuilder RequireX402Payment<TBuilder>(
        this TBuilder builder,
        Func<HttpContext, PaymentRequiredInfo> paymentRequiredInfoFactory,
        SettlementMode settlementMode = SettlementMode.Pessimistic)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(new X402PaymentEndpointFilter(paymentRequiredInfoFactory, settlementMode));
        return builder;
    }

    /// <summary>
    /// Requires x402 payment on this endpoint using a factory with output schema customization.
    /// </summary>
    public static TBuilder RequireX402Payment<TBuilder>(
        this TBuilder builder,
        Func<HttpContext, PaymentRequiredInfo> paymentRequiredInfoFactory,
        SettlementMode settlementMode,
        Func<HttpContext, PaymentRequirements, OutputSchema, OutputSchema> onSetOutputSchema)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(new X402PaymentEndpointFilter(paymentRequiredInfoFactory, settlementMode, onSetOutputSchema: onSetOutputSchema));
        return builder;
    }

    /// <summary>
    /// Requires x402 payment on this endpoint using a factory with settlement callback.
    /// </summary>
    public static TBuilder RequireX402Payment<TBuilder>(
        this TBuilder builder,
        Func<HttpContext, PaymentRequiredInfo> paymentRequiredInfoFactory,
        SettlementMode settlementMode,
        Func<HttpContext, SettlementResponse?, Exception?, Task> onSettlement)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(new X402PaymentEndpointFilter(paymentRequiredInfoFactory, settlementMode, onSettlement));
        return builder;
    }

    /// <summary>
    /// Requires x402 payment on this endpoint using a factory with full customization.
    /// </summary>
    public static TBuilder RequireX402Payment<TBuilder>(
        this TBuilder builder,
        Func<HttpContext, PaymentRequiredInfo> paymentRequiredInfoFactory,
        SettlementMode settlementMode,
        Func<HttpContext, SettlementResponse?, Exception?, Task> onSettlement,
        Func<HttpContext, PaymentRequirements, OutputSchema, OutputSchema> onSetOutputSchema)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(new X402PaymentEndpointFilter(paymentRequiredInfoFactory, settlementMode, onSettlement, onSetOutputSchema));
        return builder;
    }
}
