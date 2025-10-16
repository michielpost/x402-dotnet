using x402.Client.Models;
using x402.Core.Models;

namespace x402.Client.Tests.Wallet
{
    public class TestWallet : BaseWallet
    {
        public TestWallet(List<AssetAllowance> assetAllowances)
        {
            AssetAllowances = assetAllowances;
        }

        protected override PaymentPayloadHeader CreateHeader(PaymentRequirements requirement, CancellationToken cancellationToken)
        {
            var header = new PaymentPayloadHeader()
            {
                Network = requirement.Network,
                Scheme = requirement.Scheme,
                X402Version = 1,
                Payload = new Payload
                {
                    Resource = requirement.Resource,
                    Signature = "",
                    Authorization = new Authorization
                    {
                        Value = "1",
                        From = "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37",
                        To = requirement.PayTo,

                    }
                },
            };

            return header;
        }
    }
}
