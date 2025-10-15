using x402.Models;

namespace x402.Client.EVM
{
    public class EVMWallet : BaseWallet
    {
        protected override Task<PaymentPayloadHeader> CreateHeaderAsync(PaymentRequirements requirement, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
