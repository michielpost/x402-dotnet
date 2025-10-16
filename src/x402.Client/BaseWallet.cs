using x402.Client.Models;
using x402.Core.Models;

namespace x402.Client
{
    public abstract class BaseWallet : IX402Wallet
    {
        public List<AssetAllowance> AssetAllowances { get; set; } = new();
        public bool IgnoreAllowances { get; set; }

        public virtual (PaymentRequirements? Requirement, PaymentPayloadHeader? Header) RequestPayment(IReadOnlyList<PaymentRequirements> requirements, CancellationToken cancellationToken)
        {
            var allowedRequirements = requirements
                .Where(r => AssetAllowances.Any(a =>
                    a.Asset == r.Asset
                    && a.TotalAllowance >= long.Parse(r.MaxAmountRequired)
                    && a.MaxPerRequestAllowance >= long.Parse(r.MaxAmountRequired))
                || IgnoreAllowances)
                .ToList();

            var selectedRequirement = allowedRequirements.FirstOrDefault();

            if (selectedRequirement == null)
                return (null, null);

            var header = CreateHeader(selectedRequirement, cancellationToken);
            return (selectedRequirement, header);
        }

        protected abstract PaymentPayloadHeader CreateHeader(PaymentRequirements requirement, CancellationToken cancellationToken);
    }
}


