using System.Numerics;

namespace x402.Client.Nano
{
    /// <summary>
    /// Spending limits of a <see cref="NanoWallet"/>, in raw. Raw amounts do not fit in a
    /// <see cref="long"/> (1 XNO = 10^30 raw), so Nano uses its own allowance instead of
    /// <see cref="Models.AssetAllowance"/>.
    /// </summary>
    public record NanoAllowance
    {
        /// <summary>What the wallet may still spend in total. Lowered after every payment.</summary>
        public required BigInteger TotalAllowanceRaw { get; set; }

        /// <summary>The most the wallet pays for a single request.</summary>
        public required BigInteger MaxPerRequestAllowanceRaw { get; set; }
    }
}
