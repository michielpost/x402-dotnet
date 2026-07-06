using Microsoft.AspNetCore.Http;
using x402.Core.Models;
using x402.Core.Models.v2;

namespace x402
{
    public static class HttpContextExtensions
    {
        public static readonly string SettlementOverridesKey = "X402SettlementOverrides";

        public static X402ProcessingResult? GetX402ResultV2(this HttpContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (context.Items.TryGetValue(X402HandlerV2.X402ResultKey, out var result) && result is X402ProcessingResult x402Result)
            {
                return x402Result;
            }

            return null;
        }

        /// <summary>
        /// Specifies the actual amount to settle for the current request. Used with the "upto" and
        /// "batch-settlement" schemes to charge only what was actually used instead of the authorized maximum.
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        /// <param name="overrides">The overrides, including the amount as raw atomic units (e.g. "1000"), a percentage of the authorized maximum (e.g. "50%"), or a dollar price (e.g. "$0.05").</param>
        public static void SetSettlementOverrides(this HttpContext context, SettlementOverrides overrides)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (overrides == null)
            {
                throw new ArgumentNullException(nameof(overrides));
            }

            context.Items[SettlementOverridesKey] = overrides;
        }

        /// <inheritdoc cref="SetSettlementOverrides(HttpContext, SettlementOverrides)"/>
        /// <param name="context">The current HTTP context.</param>
        /// <param name="amount">Raw atomic units (e.g. "1000"), a percentage of the authorized maximum (e.g. "50%"), or a dollar price (e.g. "$0.05").</param>
        public static void SetSettlementOverrides(this HttpContext context, string amount)
        {
            context.SetSettlementOverrides(new SettlementOverrides { Amount = amount });
        }

        /// <summary>
        /// Returns the settlement overrides set for the current request, if any.
        /// </summary>
        public static SettlementOverrides? GetSettlementOverrides(this HttpContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (context.Items.TryGetValue(SettlementOverridesKey, out var result) && result is SettlementOverrides overrides)
            {
                return overrides;
            }

            return null;
        }
    }
}
