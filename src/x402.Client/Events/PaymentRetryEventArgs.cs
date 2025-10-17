namespace x402.Client.Events
{
    public class PaymentRetryEventArgs : EventArgs
    {
        public HttpRequestMessage Request { get; }
        public int Attempt { get; }

        public PaymentRetryEventArgs(HttpRequestMessage request, int attempt)
        {
            Request = request;
            Attempt = attempt;
        }
    }
}
