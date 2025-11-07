using x402.Client.Events;
using x402.Client.v1.Events;

namespace x402.Client.v1
{
    public class WalletProvider : IWalletProvider
    {
        public IX402WalletV1? Wallet { get; set; }

        public WalletProvider(IX402WalletV1? wallet = null)
        {
            Wallet = wallet;
        }

        public event PaymentRequiredEventHandler? PaymentRequiredReceived;
        public event EventHandler<PaymentSelectedEventArgs>? PaymentSelected;
        public event EventHandler<PaymentRetryEventArgs>? PaymentRetrying;

        public virtual bool RaiseOnPaymentRequiredReceived(PaymentRequiredEventArgs e)
        {
            var canContinue = true;
            if (PaymentRequiredReceived != null)
            {
                // If any subscriber returns false, we should not continue
                foreach (PaymentRequiredEventHandler handler in PaymentRequiredReceived.GetInvocationList())
                {
                    if (!handler(this, e))
                    {
                        canContinue = false;
                        break;
                    }
                }
            }
            return canContinue;
        }

        public virtual void RaiseOnPaymentSelected(PaymentSelectedEventArgs e)
            => PaymentSelected?.Invoke(this, e);

        public virtual void RaiseOnPaymentRetrying(PaymentRetryEventArgs e)
            => PaymentRetrying?.Invoke(this, e);

    }
}
