using System.Globalization;
using System.Numerics;

namespace x402.Core
{
    /// <summary>
    /// Resolves a settlement override amount ("1000", "50%", "$0.05") to atomic units.
    /// </summary>
    public static class SettlementAmountResolver
    {
        /// <summary>
        /// Resolves an override amount against the authorized maximum.
        /// </summary>
        /// <param name="amount">Raw atomic units (e.g. "1000"), a percentage of the authorized maximum (e.g. "50%" or "33.33%"), or a dollar price (e.g. "$0.05").</param>
        /// <param name="maxAtomicAmount">The authorized maximum in atomic units.</param>
        /// <param name="assetDecimals">The asset's decimals; required to resolve dollar prices.</param>
        /// <returns>The resolved amount in atomic units. Always &gt;= 0 and &lt;= the authorized maximum.</returns>
        /// <exception cref="FormatException">If the amount cannot be parsed.</exception>
        /// <exception cref="InvalidOperationException">If the resolved amount exceeds the authorized maximum, or a dollar price is used without asset decimals.</exception>
        public static BigInteger Resolve(string amount, string maxAtomicAmount, int? assetDecimals = null)
        {
            if (string.IsNullOrWhiteSpace(amount))
                throw new FormatException("Settlement override amount is empty");

            if (!BigInteger.TryParse(maxAtomicAmount, NumberStyles.None, CultureInfo.InvariantCulture, out var max) || max < BigInteger.Zero)
                throw new FormatException($"Invalid authorized maximum amount \"{maxAtomicAmount}\"");

            amount = amount.Trim();

            BigInteger resolved;
            if (amount.EndsWith('%'))
            {
                resolved = ResolvePercentage(amount, max);
            }
            else if (amount.StartsWith('$'))
            {
                resolved = ResolveDollarPrice(amount, assetDecimals);
            }
            else
            {
                if (!BigInteger.TryParse(amount, NumberStyles.None, CultureInfo.InvariantCulture, out resolved))
                    throw new FormatException($"Invalid settlement override amount \"{amount}\"");
            }

            if (resolved > max)
                throw new InvalidOperationException($"Resolved settlement amount {resolved} exceeds the authorized maximum {max}");

            return resolved;
        }

        private static BigInteger ResolvePercentage(string amount, BigInteger max)
        {
            var percentText = amount[..^1].Trim();
            if (!decimal.TryParse(percentText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var percent))
                throw new FormatException($"Invalid percentage \"{amount}\"");

            if (percent < 0m)
                throw new FormatException($"Percentage cannot be negative: \"{amount}\"");

            if (decimal.Round(percent, 2) != percent)
                throw new FormatException($"Percentage supports up to two decimal places: \"{amount}\"");

            // Scale to basis points of a percent (x100) to stay in integer math, then floor.
            var scaledPercent = new BigInteger(percent * 100m); // e.g. 33.33% -> 3333
            return max * scaledPercent / 10000;
        }

        private static BigInteger ResolveDollarPrice(string amount, int? assetDecimals)
        {
            if (assetDecimals == null)
                throw new InvalidOperationException("Cannot resolve a dollar-denominated settlement amount without asset decimals");

            var priceText = amount[1..].Trim();
            if (!decimal.TryParse(priceText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price) || price < 0m)
                throw new FormatException($"Invalid dollar price \"{amount}\"");

            // price * 10^decimals, floored to the nearest atomic unit
            var atomic = price;
            for (int i = 0; i < assetDecimals.Value; i++)
            {
                atomic *= 10m;
            }
            return new BigInteger(decimal.Floor(atomic));
        }
    }
}
