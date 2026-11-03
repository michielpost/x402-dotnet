using System.Globalization;
using System.Numerics;
using x402.Core.Enums;
using x402.Core.Models.v2;

namespace x402.Client.Nano
{
    /// <summary>
    /// An x402 wallet that pays the "exact" scheme on Nano (XNO).
    ///
    /// Nano has no transfer authorizations: the payer publishes its own send block and
    /// hands the block hash to the server, which checks it on the ledger. Creating the
    /// header therefore moves the funds, through the <see cref="INanoSender"/>.
    /// </summary>
    public class NanoWallet : BaseWallet
    {
        private const string Alphabet = "13456789abcdefghijkmnopqrstuwxyz";

        private readonly INanoSender sender;
        private readonly object allowanceLock = new();

        /// <summary>The account that pays, <c>nano_...</c>. Sent as <c>authorization.from</c>.</summary>
        public string Account { get; }

        /// <summary>
        /// Spending limits in raw. Ignored when <see cref="BaseWallet.IgnoreAllowances"/> is true;
        /// when it is false and no allowance is set, the wallet pays nothing.
        /// </summary>
        public NanoAllowance? Allowance { get; set; }

        /// <param name="account">The paying account, <c>nano_...</c>.</param>
        /// <param name="sender">Publishes the send block; it holds the key.</param>
        /// <param name="network">CAIP-2 network identifier, <see cref="NanoNetworks.Mainnet"/>.</param>
        public NanoWallet(string account, INanoSender sender, string network = NanoNetworks.Mainnet) : base(network)
        {
            ArgumentNullException.ThrowIfNull(sender);
            if (!IsAccount(account))
                throw new ArgumentException($"'{account}' is not a Nano account.", nameof(account));

            Account = account.Trim().ToLowerInvariant();
            this.sender = sender;
        }

        /// <summary>
        /// Picks the first requirement this wallet can pay: exact scheme, this network,
        /// asset XNO, a valid <c>payTo</c> account and a positive integer amount within the allowance.
        /// </summary>
        public override Task<PaymentRequirements?> SelectPaymentAsync(PaymentRequiredResponse paymentRequiredResponse, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(paymentRequiredResponse);

            var selected = paymentRequiredResponse.Accepts
                .FirstOrDefault(r => Check(r, out var amount) == null && WithinAllowance(amount));

            return Task.FromResult(selected);
        }

        /// <summary>
        /// Publishes a send of exactly <c>amount</c> raw to <c>payTo</c> and returns the header
        /// carrying the block hash. The funds move here, before the request is retried.
        /// </summary>
        /// <remarks>
        /// Call this once per requirement. Every call is a new payment: it passes a fresh
        /// idempotency id to the <see cref="INanoSender"/>, so calling it again for the same
        /// requirement (for example to retry after an error) sends the amount a second time.
        /// </remarks>
        public override async Task<PaymentPayloadHeader> CreateHeaderAsync(PaymentRequirements requirement, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requirement);

            var error = Check(requirement, out var amount);
            if (error != null)
                throw new ArgumentException(error, nameof(requirement));

            ReserveAllowance(amount);

            // The allowance is not given back when the send fails: a send that timed out
            // may still have been published, and counting it is the safe side.
            var payTo = requirement.PayTo.Trim().ToLowerInvariant();
            var blockHash = await sender.SendAsync(payTo, amount, Guid.NewGuid().ToString("N"), cancellationToken).ConfigureAwait(false);

            blockHash = blockHash?.Trim().ToUpperInvariant() ?? string.Empty;
            if (blockHash.Length != 64 || !blockHash.All(char.IsAsciiHexDigit))
                throw new InvalidOperationException($"The Nano sender returned '{blockHash}', which is not a block hash.");

            return new PaymentPayloadHeader
            {
                X402Version = 2,
                Accepted = requirement,
                Payload = new Payload
                {
                    BlockHash = blockHash,
                    Authorization = new Authorization
                    {
                        From = Account,
                        To = payTo,
                        Value = amount.ToString(CultureInfo.InvariantCulture)
                    }
                }
            };
        }

        /// <summary>
        /// Checks the format of a Nano account: <c>nano_</c> or <c>xrb_</c>, then 60 base32 characters.
        /// The checksum is not checked; the node that publishes the block rejects a bad one.
        /// </summary>
        public static bool IsAccount(string? account)
        {
            if (string.IsNullOrWhiteSpace(account))
                return false;

            var value = account.Trim().ToLowerInvariant();
            string body;
            if (value.StartsWith("nano_", StringComparison.Ordinal))
                body = value[5..];
            else if (value.StartsWith("xrb_", StringComparison.Ordinal))
                body = value[4..];
            else
                return false;

            return body.Length == 60 && (body[0] == '1' || body[0] == '3') && body.All(c => Alphabet.Contains(c));
        }

        private string? Check(PaymentRequirements requirement, out BigInteger amount)
        {
            amount = BigInteger.Zero;

            if (requirement.Scheme != PaymentScheme.Exact)
                return $"Scheme '{requirement.Scheme}' is not supported on Nano; only exact is.";

            if (requirement.Network != Network)
                return $"Network '{requirement.Network}' is not this wallet's network '{Network}'.";

            if (!string.Equals(requirement.Asset, NanoNetworks.AssetXno, StringComparison.OrdinalIgnoreCase))
                return $"Asset '{requirement.Asset}' is not {NanoNetworks.AssetXno}.";

            if (!IsAccount(requirement.PayTo))
                return $"payTo '{requirement.PayTo}' is not a Nano account.";

            // Digits only: no sign, no decimal point, no exponent. Raw is never a fraction.
            if (!BigInteger.TryParse(requirement.Amount, NumberStyles.None, CultureInfo.InvariantCulture, out amount)
                || amount <= BigInteger.Zero)
                return $"Amount '{requirement.Amount}' is not a positive integer in raw.";

            return null;
        }

        private bool WithinAllowance(BigInteger amount)
        {
            if (IgnoreAllowances)
                return true;

            lock (allowanceLock)
            {
                return Allowance != null
                    && amount <= Allowance.MaxPerRequestAllowanceRaw
                    && amount <= Allowance.TotalAllowanceRaw;
            }
        }

        private void ReserveAllowance(BigInteger amount)
        {
            if (IgnoreAllowances)
                return;

            lock (allowanceLock)
            {
                if (Allowance == null
                    || amount > Allowance.MaxPerRequestAllowanceRaw
                    || amount > Allowance.TotalAllowanceRaw)
                    throw new InvalidOperationException($"Paying {amount} raw exceeds the wallet's Nano allowance.");

                Allowance.TotalAllowanceRaw -= amount;
            }
        }
    }
}
