using x402.Core.Models.v2;

namespace x402.Client.v2.Events
{
    public class PaymentSelectedEventArgs : EventArgs
    {
        public HttpRequestMessage Request { get; }
        public PaymentRequirements? PaymentRequirements { get; }
        public PaymentPayloadHeader? PaymentHeader { get; }
        public int Attempt { get; }

        public PaymentSelectedEventArgs(HttpRequestMessage request, PaymentRequirements? paymentRequirements, PaymentPayloadHeader? paymentHeader, int attempt)
        {
            Request = request;
            PaymentRequirements = paymentRequirements;
            PaymentHeader = paymentHeader;
            Attempt = attempt;
        }
    }
}
