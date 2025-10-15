using x402.Client.Models;
using x402.Models;

namespace x402.Client
{
    public abstract class BaseWallet : IX402Wallet
    {
        public List<AssetAllowance> AssetAllowances { get; set; } = new();
        public bool IgnoreAllowances { get; set; }

        public async virtual Task<(PaymentRequirements? Requirement, PaymentPayloadHeader? Header)> RequestPaymentAsync(IReadOnlyList<PaymentRequirements> requirements, CancellationToken cancellationToken)
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

            var header = await CreateHeaderAsync(selectedRequirement, cancellationToken).ConfigureAwait(false);
            return (selectedRequirement, header);
        }

        protected abstract Task<PaymentPayloadHeader> CreateHeaderAsync(PaymentRequirements requirement, CancellationToken cancellationToken);
    }
}


