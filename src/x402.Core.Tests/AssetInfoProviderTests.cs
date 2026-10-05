namespace x402.Core.Tests;

using x402.Core;

public class AssetInfoProviderTests
{

    [Test]
    public void ContractsAreUnique()
    {
        // Arrange
        var provider = new AssetInfoProvider();

        // Act
        var contracts = provider.GetAll().Select(a => a.ContractAddress).ToList();

        // Assert
        Assert.That(contracts.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(contracts.Count), "All contract addresses should be unique (case-insensitive).");
    }

    [Test]
    public void Nano_IsKnownWithThirtyDecimals()
    {
        // Arrange
        var provider = new AssetInfoProvider();

        // Act
        var xno = provider.GetAssetInfo("XNO");

        // Assert
        Assert.That(xno, Is.Not.Null);
        Assert.That(xno!.Network, Is.EqualTo("nano:mainnet"));
        Assert.That(provider.GetAssetInfoByNetwork("nano:mainnet"), Has.Count.EqualTo(1));

        // 1 XNO = 10^30 raw
        Assert.That(System.Numerics.BigInteger.Pow(10, xno.Decimals).ToString(), Is.EqualTo("1" + new string('0', 30)));
    }
}
