using System.Numerics;

namespace x402.Client.Nano
{
    /// <summary>
    /// Publishes a Nano send block for a payment. The key stays with the implementation:
    /// a node wallet, a hardware wallet or a key management service.
    /// </summary>
    public interface INanoSender
    {
        /// <summary>
        /// Sends <paramref name="amountRaw"/> raw from the wallet's account to <paramref name="destination"/>
        /// and returns the hash of the send block once the network has confirmed it.
        /// </summary>
        /// <param name="destination">The <c>payTo</c> account of the payment requirements.</param>
        /// <param name="amountRaw">The amount in raw (1 XNO = 10^30 raw).</param>
        /// <param name="id">
        /// Unique id of this payment attempt. Implementations that retry must reuse it so that
        /// one attempt never publishes two blocks.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The block hash, 64 hex characters.</returns>
        Task<string> SendAsync(string destination, BigInteger amountRaw, string id, CancellationToken cancellationToken = default);
    }
}
