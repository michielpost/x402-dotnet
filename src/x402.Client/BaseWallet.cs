using x402.Client.Models;
using x402.Client.v1;
using x402.Client.v2;

namespace x402.Client
{
    public abstract class BaseWallet : IX402WalletV1, IX402WalletV2
    {
        public List<AssetAllowance> AssetAllowances { get; set; } = new();
        public bool IgnoreAllowances { get; set; }

        #region v2

        public virtual (Core.Models.v1.PaymentRequirements? Requirement, Core.Models.v1.PaymentPayloadHeader? Header) RequestPayment(Core.Models.v1.PaymentRequiredResponse paymentRequiredResponse, CancellationToken cancellationToken = default)
        {
            var allowedRequirements = paymentRequiredResponse.Accepts
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

        public abstract Core.Models.v1.PaymentPayloadHeader CreateHeader(Core.Models.v1.PaymentRequirements requirement, CancellationToken cancellationToken = default);
        #endregion


        #region v2
        public virtual (Core.Models.v2.PaymentRequirements? Requirement, Core.Models.v2.PaymentPayloadHeader? Header) RequestPayment(Core.Models.v2.PaymentRequiredResponse paymentRequiredResponse, CancellationToken cancellationToken = default)
        {
            var allowedRequirements = paymentRequiredResponse.Accepts
                .Where(r => AssetAllowances.Any(a =>
                    a.Asset == r.Asset
                    && a.TotalAllowance >= long.Parse(r.Amount)
                    && a.MaxPerRequestAllowance >= long.Parse(r.Amount))
                || IgnoreAllowances)
                .ToList();

            var selectedRequirement = allowedRequirements.FirstOrDefault();

            if (selectedRequirement == null)
                return (null, null);

            var header = CreateHeader(selectedRequirement, cancellationToken);
            return (selectedRequirement, header);
        }

        public abstract Core.Models.v2.PaymentPayloadHeader CreateHeader(Core.Models.v2.PaymentRequirements requirement, CancellationToken cancellationToken = default);
        #endregion
    }
}


