using x402.Core.Models.v1;

namespace x402.Client.v1
{
    public static class HttpRequestMessageExtensions
    {
        public const string PaymentHeaderV1 = "X-PAYMENT";

        public static void AddPaymentHeader(this HttpRequestMessage request, PaymentPayloadHeader header)
        {
            // Use the appropriate header based on X402 version
            request.Headers.Add(PaymentHeaderV1, header.ToBase64Header());
        }
    }
}
