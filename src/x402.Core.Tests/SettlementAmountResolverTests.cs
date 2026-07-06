using System.Numerics;
using x402.Core;

namespace x402.Core.Tests
{
    [TestFixture]
    public class SettlementAmountResolverTests
    {
        [Test]
        public void Resolve_RawAtomicUnits_ReturnsExactAmount()
        {
            var result = SettlementAmountResolver.Resolve("1000", "100000");

            Assert.That(result, Is.EqualTo(new BigInteger(1000)));
        }

        [Test]
        public void Resolve_RawAtomicUnits_Zero_ReturnsZero()
        {
            var result = SettlementAmountResolver.Resolve("0", "100000");

            Assert.That(result, Is.EqualTo(BigInteger.Zero));
        }

        [Test]
        public void Resolve_RawAtomicUnits_EqualToMax_IsAllowed()
        {
            var result = SettlementAmountResolver.Resolve("100000", "100000");

            Assert.That(result, Is.EqualTo(new BigInteger(100000)));
        }

        [Test]
        public void Resolve_RawAtomicUnits_ExceedsMax_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => SettlementAmountResolver.Resolve("100001", "100000"));
        }

        [Test]
        public void Resolve_Percentage_ReturnsFractionOfMax()
        {
            var result = SettlementAmountResolver.Resolve("50%", "100000");

            Assert.That(result, Is.EqualTo(new BigInteger(50000)));
        }

        [Test]
        public void Resolve_Percentage_TwoDecimals_FlooredToAtomicUnit()
        {
            // 33.33% of 100000 = 33330
            var result = SettlementAmountResolver.Resolve("33.33%", "100000");

            Assert.That(result, Is.EqualTo(new BigInteger(33330)));
        }

        [Test]
        public void Resolve_Percentage_ResultFloored()
        {
            // 33.33% of 101 = 33.6633 -> floored to 33
            var result = SettlementAmountResolver.Resolve("33.33%", "101");

            Assert.That(result, Is.EqualTo(new BigInteger(33)));
        }

        [Test]
        public void Resolve_Percentage_100Percent_ReturnsMax()
        {
            var result = SettlementAmountResolver.Resolve("100%", "100000");

            Assert.That(result, Is.EqualTo(new BigInteger(100000)));
        }

        [Test]
        public void Resolve_Percentage_ZeroPercent_ReturnsZero()
        {
            var result = SettlementAmountResolver.Resolve("0%", "100000");

            Assert.That(result, Is.EqualTo(BigInteger.Zero));
        }

        [Test]
        public void Resolve_Percentage_MoreThanTwoDecimals_Throws()
        {
            Assert.Throws<FormatException>(() => SettlementAmountResolver.Resolve("33.333%", "100000"));
        }

        [Test]
        public void Resolve_Percentage_Above100_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => SettlementAmountResolver.Resolve("101%", "100000"));
        }

        [Test]
        public void Resolve_Percentage_Negative_Throws()
        {
            Assert.Throws<FormatException>(() => SettlementAmountResolver.Resolve("-5%", "100000"));
        }

        [Test]
        public void Resolve_DollarPrice_ConvertsUsingAssetDecimals()
        {
            // $0.05 with 6 decimals = 50,000 atomic units
            var result = SettlementAmountResolver.Resolve("$0.05", "100000", assetDecimals: 6);

            Assert.That(result, Is.EqualTo(new BigInteger(50000)));
        }

        [Test]
        public void Resolve_DollarPrice_FlooredToAtomicUnit()
        {
            // $0.000000129 with 6 decimals = 0.129 atomic units -> floored to 0
            var result = SettlementAmountResolver.Resolve("$0.000000129", "100000", assetDecimals: 6);

            Assert.That(result, Is.EqualTo(BigInteger.Zero));
        }

        [Test]
        public void Resolve_DollarPrice_WithoutAssetDecimals_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => SettlementAmountResolver.Resolve("$0.05", "100000"));
        }

        [Test]
        public void Resolve_DollarPrice_ExceedsMax_Throws()
        {
            // $0.20 with 6 decimals = 200,000 > authorized 100,000
            Assert.Throws<InvalidOperationException>(() => SettlementAmountResolver.Resolve("$0.20", "100000", assetDecimals: 6));
        }

        [TestCase("")]
        [TestCase("  ")]
        [TestCase("abc")]
        [TestCase("-100")]
        [TestCase("1.5")]
        [TestCase("$abc")]
        [TestCase("%")]
        public void Resolve_InvalidFormats_Throw(string amount)
        {
            Assert.Throws<FormatException>(() => SettlementAmountResolver.Resolve(amount, "100000", assetDecimals: 6));
        }

        [Test]
        public void Resolve_InvalidMax_Throws()
        {
            Assert.Throws<FormatException>(() => SettlementAmountResolver.Resolve("1000", "not-a-number"));
        }
    }
}
