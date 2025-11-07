using x402.Client.Events;
using x402.Client.v1.Events;

namespace x402.Client.v1
{
    public interface IWalletProvider
    {
        public IX402WalletV1? Wallet { get; set; }
        
        event PaymentRequiredEventHandler? PaymentRequiredReceived;
        event EventHandler<PaymentSelectedEventArgs>? PaymentSelected;
        event EventHandler<PaymentRetryEventArgs>? PaymentRetrying;

        void RaiseOnPaymentSelected(PaymentSelectedEventArgs e);
        void RaiseOnPaymentRetrying(PaymentRetryEventArgs e);
        bool RaiseOnPaymentRequiredReceived(PaymentRequiredEventArgs paymentRequiredEventArgs);
    }
}
