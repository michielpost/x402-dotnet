using System.Text.Json;
using x402.Core.Enums;
using x402.Core.Models.v2;

namespace x402.Core.Tests
{
    [TestFixture]
    public class PaymentSchemeSerializationTests
    {
        [TestCase(PaymentScheme.Exact, "\"exact\"")]
        [TestCase(PaymentScheme.Upto, "\"upto\"")]
        [TestCase(PaymentScheme.BatchSettlement, "\"batch-settlement\"")]
        public void Serialize_WritesKebabCase(PaymentScheme scheme, string expectedJson)
        {
            var json = JsonSerializer.Serialize(scheme);

            Assert.That(json, Is.EqualTo(expectedJson));
        }

        [TestCase("\"exact\"", PaymentScheme.Exact)]
        [TestCase("\"Exact\"", PaymentScheme.Exact)]
        [TestCase("\"upto\"", PaymentScheme.Upto)]
        [TestCase("\"batch-settlement\"", PaymentScheme.BatchSettlement)]
        [TestCase("\"batchsettlement\"", PaymentScheme.BatchSettlement)]
        [TestCase("\"BatchSettlement\"", PaymentScheme.BatchSettlement)]
        public void Deserialize_AcceptsHyphenatedAndPlainVariants(string json, PaymentScheme expected)
        {
            var scheme = JsonSerializer.Deserialize<PaymentScheme>(json);

            Assert.That(scheme, Is.EqualTo(expected));
        }

        [Test]
        public void Deserialize_UnknownValue_Throws()
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PaymentScheme>("\"streaming\""));
        }

        [Test]
        public void PaymentRequirements_RoundTrips_BatchSettlementScheme()
        {
            var requirements = new PaymentRequirements
            {
                Scheme = PaymentScheme.BatchSettlement,
                Network = "eip155:84532",
                Amount = "10000",
                Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
                PayTo = "0x0000000000000000000000000000000000000001",
            };

            var json = JsonSerializer.Serialize(requirements, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var deserialized = JsonSerializer.Deserialize<PaymentRequirements>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.That(json, Does.Contain("\"batch-settlement\""));
            Assert.That(deserialized!.Scheme, Is.EqualTo(PaymentScheme.BatchSettlement));
        }

        [Test]
        public void PaymentRequirementsExtra_SerializesAssetTransferMethod()
        {
            var extra = new PaymentRequirementsExtra
            {
                Name = "USDC",
                Version = "2",
                AssetTransferMethod = AssetTransferMethods.Permit2
            };

            var json = JsonSerializer.Serialize(extra, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.That(json, Does.Contain("\"assetTransferMethod\":\"permit2\""));
        }

        [Test]
        public void PaymentRequirementsExtra_OmitsNullAssetTransferMethod()
        {
            var extra = new PaymentRequirementsExtra { Name = "USDC", Version = "2" };

            var json = JsonSerializer.Serialize(extra, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            Assert.That(json, Does.Not.Contain("assetTransferMethod"));
        }
    }
}
