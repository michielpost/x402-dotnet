using x402.Client.Tests.Wallet;
using x402.Core.Enums;
using x402.Core.Models.v1;

namespace x402.Client.Tests
{
    public class BaseWalletTests
    {
        private static PaymentRequirements Req(string asset, string amount = "1000") => new PaymentRequirements
        {
            Scheme = PaymentScheme.Exact,
            Network = "base-sepolia",
            MaxAmountRequired = amount,
            Asset = asset,
            PayTo = "0x1111111111111111111111111111111111111111",
            Resource = "/resource/protected"
        };

        [Test]
        public void Returns_Null_When_No_Allowances_And_Ignore_False()
        {
            var wallet = new TestWallet(new());
            wallet.IgnoreAllowances = false;

            var (selected, header) = wallet.RequestPayment(new[] { Req("0xA") }, CancellationToken.None);

            Assert.That(selected, Is.Null);
            Assert.That(header, Is.Null);
        }

        [Test]
        public void Selects_First_Matching_Requirement()
        {
            var wallet = new TestWallet(new()
            {
                new() { Asset = "0xB", TotalAllowance = 5_000, MaxPerRequestAllowance = 5_000 }
            });

            var r1 = Req("0xA");
            var r2 = Req("0xB");

            var (selected, header) = wallet.RequestPayment(new[] { r1, r2 }, CancellationToken.None);

            Assert.That(selected, Is.EqualTo(r2));
            Assert.That(header, Is.Not.Null);
            Assert.That(header!.Network, Is.EqualTo(r2.Network));
            Assert.That(header.Payload.Authorization.To, Is.EqualTo(r2.PayTo));
        }

        [Test]
        public void IgnoreAllowances_Bypasses_Checks()
        {
            var wallet = new TestWallet(new());
            wallet.IgnoreAllowances = true;

            var r = Req("0xZZ");
            var (selected, header) = wallet.RequestPayment(new[] { r }, CancellationToken.None);

            Assert.That(selected, Is.EqualTo(r));
            Assert.That(header, Is.Not.Null);
        }

        [Test]
        public void Enforces_Total_And_Per_Request_Allowances()
        {
            var wallet = new TestWallet(new()
            {
                new() { Asset = "0xC", TotalAllowance = 500, MaxPerRequestAllowance = 1000 },
                new() { Asset = "0xD", TotalAllowance = 10_000, MaxPerRequestAllowance = 500 }
            });

            var rC = Req("0xC", amount: "600"); // exceeds total
            var rD = Req("0xD", amount: "600"); // exceeds per-request
            var rOk = Req("0xD", amount: "400"); // within per-request

            var (selected1, header1) = wallet.RequestPayment(new[] { rC }, CancellationToken.None);
            var (selected2, header2) = wallet.RequestPayment(new[] { rD }, CancellationToken.None);
            var (selected3, header3) = wallet.RequestPayment(new[] { rC, rD, rOk }, CancellationToken.None);

            Assert.That(selected1, Is.Null);
            Assert.That(header1, Is.Null);

            Assert.That(selected2, Is.Null);
            Assert.That(header2, Is.Null);

            Assert.That(selected3, Is.EqualTo(rOk));
            Assert.That(header3, Is.Not.Null);
        }
    }
}


