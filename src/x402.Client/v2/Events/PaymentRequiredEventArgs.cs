using x402.Core.Models.v2;

namespace x402.Client.v2.Events
{
    public delegate bool PaymentRequiredEventHandler(object sender, PaymentRequiredEventArgs e);

    public class PaymentRequiredEventArgs : EventArgs
    {
        public HttpRequestMessage Request { get; }
        public HttpResponseMessage Response { get; }
        public PaymentRequiredResponse PaymentRequiredResponse { get; }

        public PaymentRequiredEventArgs(HttpRequestMessage request, HttpResponseMessage response, PaymentRequiredResponse paymentRequiredResponse)
        {
            Request = request;
            Response = response;
            PaymentRequiredResponse = paymentRequiredResponse;
        }
    }
}
