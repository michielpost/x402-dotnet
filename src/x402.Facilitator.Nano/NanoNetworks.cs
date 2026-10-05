using System.Numerics;

namespace x402.Facilitator.Nano;

/// <summary>
/// Network and asset identifiers for the x402 "exact" scheme on Nano.
/// </summary>
public static class NanoNetworks
{
    /// <summary>CAIP-2 identifier of Nano mainnet.</summary>
    public const string Mainnet = "nano:mainnet";

    /// <summary>The native asset. Nano has no tokens, so there is no contract address.</summary>
    public const string AssetXno = "XNO";

    /// <summary>Number of decimals of XNO: 1 XNO = 10^30 raw.</summary>
    public const int XnoDecimals = 30;

    /// <summary>Raw units in one XNO. Amounts on the wire are always integer strings in raw.</summary>
    public static readonly BigInteger RawPerXno = BigInteger.Pow(10, XnoDecimals);
}
