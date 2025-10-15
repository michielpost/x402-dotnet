using x402.Client;
using x402.Client.EVM;
using x402.Enums;
using x402.Models;

namespace x402.Client.Tests
{
    public class EVMWalletTests
    {
        private static PaymentRequirements BuildRequirement()
        {
            return new PaymentRequirements
            {
                Scheme = PaymentScheme.Exact,
                Network = "base-sepolia",
                MaxAmountRequired = "1000",
                Asset = "0x0000000000000000000000000000000000000000",
                PayTo = "0x1111111111111111111111111111111111111111",
                Resource = "/resource/protected"
            };
        }

        [Test]
        public async Task RequestPaymentAsync_BuildsHeaderWithExpectedMappings()
        {
            // Arrange
            var requirement = BuildRequirement();
            var requirements = new List<PaymentRequirements> { requirement };

            // Fixed private key (32 bytes hex) for deterministic address; signature will still vary due to nonce/time
            var wallet = new EVMWallet("0x0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")
            {
                IgnoreAllowances = true
            };

            // Act
            var (selected, header) = await wallet.RequestPaymentAsync(requirements, CancellationToken.None);

            // Assert
            Assert.That(selected, Is.Not.Null);
            Assert.That(header, Is.Not.Null);

            Assert.That(header!.Network, Is.EqualTo(requirement.Network));
            Assert.That(header.Scheme, Is.EqualTo(requirement.Scheme));
            Assert.That(header.X402Version, Is.EqualTo(1));

            Assert.That(header.Payload, Is.Not.Null);
            Assert.That(header.Payload.Resource, Is.EqualTo(requirement.Resource));
            Assert.That(string.IsNullOrWhiteSpace(header.Payload.Signature), Is.False);

            var auth = header.Payload.Authorization;
            Assert.That(auth, Is.Not.Null);
            Assert.That(auth.To, Is.EqualTo(requirement.PayTo));

            // Basic format checks
            Assert.That(auth.From, Does.StartWith("0x"));
            Assert.That(auth.From!.Length, Is.EqualTo(42)); // 0x + 40 hex chars
            Assert.That(auth.Nonce, Does.StartWith("0x"));
            Assert.That(auth.Nonce!.Length, Is.EqualTo(66)); // 0x + 64 hex chars (bytes32)
            Assert.That(long.Parse(auth.ValidBefore), Is.GreaterThan(long.Parse(auth.ValidAfter)));
        }

        [Test]
        public async Task RequestPaymentAsync_GeneratesEip712SignatureFormat()
        {
            // Arrange
            var requirements = new List<PaymentRequirements> { BuildRequirement() };
            var wallet = new EVMWallet("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")
            {
                IgnoreAllowances = true
            };

            // Act
            var (_, header) = await wallet.RequestPaymentAsync(requirements, CancellationToken.None);

            // Assert
            Assert.That(header, Is.Not.Null);
            var sig = header!.Payload.Signature;
            Assert.That(sig, Does.StartWith("0x"));
            Assert.That(sig.Length, Is.EqualTo(132)); // 0x + 130 hex chars (65 bytes)
        }
    }
}


