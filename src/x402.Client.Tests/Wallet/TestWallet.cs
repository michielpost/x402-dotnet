using x402.Client.Models;

namespace x402.Client.Tests.Wallet
{
    public class TestWallet : BaseWallet
    {
        public int Version { get; set; } = 1;

        public TestWallet(List<AssetAllowance> assetAllowances)
        {
            AssetAllowances = assetAllowances;
        }

        public override Core.Models.v1.PaymentPayloadHeader CreateHeader(Core.Models.v1.PaymentRequirements requirement, CancellationToken cancellationToken)
        {
            var header = new Core.Models.v1.PaymentPayloadHeader()
            {
                Network = requirement.Network,
                Scheme = requirement.Scheme,
                X402Version = 1,
                Payload = new Core.Models.v1.Payload
                {
                    Resource = requirement.Resource,
                    Signature = "",
                    Authorization = new Core.Models.v1.Authorization
                    {
                        Value = "1",
                        From = "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37",
                        To = requirement.PayTo,

                    }
                },
            };

            return header;
        }

        public override Core.Models.v2.PaymentPayloadHeader CreateHeader(Core.Models.v2.PaymentRequirements requirement, CancellationToken cancellationToken = default)
        {
            var header = new Core.Models.v2.PaymentPayloadHeader()
            {
                X402Version = 2,
                Accepted = requirement,                
                Payload = new Core.Models.v2.Payload
                {
                    Signature = "",
                    Authorization = new Core.Models.v2.Authorization
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
