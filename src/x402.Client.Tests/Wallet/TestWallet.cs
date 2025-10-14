using x402.Client.Models;
using x402.Models;

namespace x402.Client.Tests.Wallet
{
    public class TestWallet : IX402Wallet
    {
        public TestWallet(List<AssetAllowance> assetAllowances)
        {
            AssetAllowances = assetAllowances;
        }

        public List<AssetAllowance> AssetAllowances { get; set; }

        public async Task<(PaymentRequirements? Requirement, PaymentPayloadHeader? Header)>
         RequestPaymentAsync(IReadOnlyList<PaymentRequirements> requirements, CancellationToken cancellationToken)
        {
            var allowedRequirements = requirements
                .Where(r => AssetAllowances.Any(a =>
                    a.Asset == r.Asset
                    && a.TotalAllowance >= long.Parse(r.MaxAmountRequired)
                    && a.MaxPerRequestAllowance >= long.Parse(r.MaxAmountRequired))
                )
                .ToList();

            var selectedRequirement = allowedRequirements.FirstOrDefault();


            if (selectedRequirement == null)
                return (null, null);

            // Sign payment for selected requirement

            // For testing purposes, we just return a dummy header
            var header = new PaymentPayloadHeader()
            {
                Payload = new Payload
                {
                    Authorization = new Authorization
                    {
                        Value = "TEST_PAYMENT"
                    }
                },
            };

            return (selectedRequirement, header);
        }
    }
}
