using Microsoft.AspNetCore.Http;
using x402.Core.Models;

namespace x402
{
    public static class HttpContextExtensions
    {
        /// <summary>
        /// Retrieves the X402 payment handling result from the HttpContext.
        /// </summary>
        /// <param name="context">The HttpContext instance.</param>
        /// <returns>The X402ProcessingResult if available; otherwise, null.</returns>
        public static X402ProcessingResult? GetX402Result(this HttpContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (context.Items.TryGetValue(X402Handler.X402ResultKey, out var result) && result is X402ProcessingResult x402Result)
            {
                return x402Result;
            }

            return null;
        }
    }
}
