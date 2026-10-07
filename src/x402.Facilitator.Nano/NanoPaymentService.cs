using System.Globalization;
using System.Numerics;
using x402.Core.Enums;
using x402.Core.Models.Facilitator;
using x402.Core.Models.v2;

namespace x402.Facilitator.Nano;

/// <summary>
/// The "exact" scheme on Nano (XNO).
///
/// Nano has no fees and no contracts: the payer publishes its own send block and
/// puts the block hash in <see cref="Payload.BlockHash"/>. Nothing is left to
/// broadcast, so this service only reads the ledger and never holds a key.
/// A payment is valid when every configured node reports the block as a confirmed
/// send of exactly <see cref="PaymentRequirements.Amount"/> raw to
/// <see cref="PaymentRequirements.PayTo"/>, and the block has not settled a payment before.
/// The block hash is returned as the settlement transaction.
/// </summary>
public class NanoPaymentService : IPaymentService
{
    private readonly IReadOnlyList<INanoRpcClient> _rpcClients;
    private readonly INanoClaimStore _claimStore;

    /// <param name="rpcClients">
    /// The nodes to ask. All of them must agree; use nodes run by different operators
    /// so that a single node cannot make a payment look valid.
    /// </param>
    /// <param name="claimStore">Where used block hashes are remembered.</param>
    public NanoPaymentService(IEnumerable<INanoRpcClient> rpcClients, INanoClaimStore claimStore)
    {
        ArgumentNullException.ThrowIfNull(rpcClients);
        ArgumentNullException.ThrowIfNull(claimStore);

        _rpcClients = rpcClients.ToList();
        if (_rpcClients.Count == 0)
            throw new ArgumentException("At least one Nano RPC client is required.", nameof(rpcClients));

        _claimStore = claimStore;
    }

    public async Task<VerificationResponse> VerifyPayment(PaymentPayloadHeader payload, PaymentRequirements requirements)
    {
        var (response, _) = await VerifyBlock(payload, requirements);
        return response;
    }

    public async Task<SettlementResponse> SettlePayment(PaymentPayloadHeader payload, PaymentRequirements requirements)
    {
        // Never rely on an earlier verify: read the ledger again.
        var (verifyResponse, blockHash) = await VerifyBlock(payload, requirements);

        if (verifyResponse.IsValid && !_claimStore.TryClaim(blockHash!))
        {
            verifyResponse = Invalid(NanoErrorCodes.BlockAlreadySettled, verifyResponse.Payer);
        }

        if (!verifyResponse.IsValid)
        {
            return new SettlementResponse
            {
                Success = false,
                ErrorReason = verifyResponse.InvalidReason,
                Network = payload.Accepted.Network,
                Transaction = "",
                Payer = verifyResponse.Payer
            };
        }

        // The payer already published the block; the hash is the receipt.
        return new SettlementResponse
        {
            Success = true,
            ErrorReason = null,
            Network = payload.Accepted.Network,
            Transaction = blockHash,
            Payer = verifyResponse.Payer
        };
    }

    private async Task<(VerificationResponse Response, string? BlockHash)> VerifyBlock(
        PaymentPayloadHeader payload,
        PaymentRequirements requirements)
    {
        var claimedPayer = payload.ExtractPayerFromPayload();

        var envelopeError = VerifyEnvelope(payload, requirements, out var requiredAmount);
        if (envelopeError != null)
            return (Invalid(envelopeError, claimedPayer), null);

        // Upper case, so that two spellings of one hash share one claim.
        var blockHash = payload.Payload.BlockHash?.Trim().ToUpperInvariant();
        if (!IsBlockHash(blockHash))
            return (Invalid(NanoErrorCodes.InvalidBlockHash, claimedPayer), null);

        if (_claimStore.IsClaimed(blockHash!))
            return (Invalid(NanoErrorCodes.BlockAlreadySettled, claimedPayer), null);

        var payTo = NormalizeAccount(requirements.PayTo);
        string? payer = null;

        foreach (var rpcClient in _rpcClients)
        {
            NanoBlockInfo? block;
            try
            {
                block = await rpcClient.GetBlockInfoAsync(blockHash!);
            }
            catch (Exception e)
            {
                // A node that cannot answer is never read as agreement.
                Console.WriteLine($"Unexpected verify error: {e.Message}");
                return (Invalid(FacilitatorErrorCodes.UnexpectedVerifyError, claimedPayer), null);
            }

            if (block == null || block.Subtype != "send" || !block.Confirmed)
                return (Invalid(FacilitatorErrorCodes.InvalidTransactionState, claimedPayer), null);

            if (block.Destination == null || NormalizeAccount(block.Destination) != payTo)
                return (Invalid(NanoErrorCodes.RecipientMismatch, claimedPayer), null);

            if (block.AmountRaw != requiredAmount)
                return (Invalid(NanoErrorCodes.AmountMismatch, claimedPayer), null);

            if (string.IsNullOrWhiteSpace(block.Account))
                return (Invalid(FacilitatorErrorCodes.InvalidTransactionState, claimedPayer), null);

            // Nodes must describe the same block.
            if (payer != null && NormalizeAccount(block.Account) != NormalizeAccount(payer))
                return (Invalid(FacilitatorErrorCodes.InvalidTransactionState, claimedPayer), null);

            payer ??= block.Account;
        }

        // The ledger says who paid. A payload that names somebody else is refused.
        if (!string.IsNullOrWhiteSpace(claimedPayer) && NormalizeAccount(claimedPayer) != NormalizeAccount(payer!))
            return (Invalid(FacilitatorErrorCodes.InvalidPayload, payer), null);

        return (new VerificationResponse { IsValid = true, Payer = payer }, blockHash);
    }

    private static string? VerifyEnvelope(PaymentPayloadHeader payload, PaymentRequirements requirements, out BigInteger requiredAmount)
    {
        requiredAmount = BigInteger.Zero;

        if (payload.Accepted.Scheme != PaymentScheme.Exact || requirements.Scheme != PaymentScheme.Exact)
            return FacilitatorErrorCodes.UnsupportedScheme;

        if (payload.Accepted.Network != requirements.Network || requirements.Network != NanoNetworks.Mainnet)
            return FacilitatorErrorCodes.InvalidNetwork;

        if (!string.Equals(requirements.Asset, NanoNetworks.AssetXno, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(payload.Accepted.Asset, NanoNetworks.AssetXno, StringComparison.OrdinalIgnoreCase))
            return FacilitatorErrorCodes.InvalidPaymentRequirements;

        if (string.IsNullOrWhiteSpace(requirements.PayTo)
            || NormalizeAccount(payload.Accepted.PayTo ?? string.Empty) != NormalizeAccount(requirements.PayTo))
            return FacilitatorErrorCodes.InvalidPaymentRequirements;

        // Digits only: no sign, no decimal point, no exponent.
        if (!BigInteger.TryParse(requirements.Amount, NumberStyles.None, CultureInfo.InvariantCulture, out requiredAmount)
            || requiredAmount <= BigInteger.Zero
            || !BigInteger.TryParse(payload.Accepted.Amount, NumberStyles.None, CultureInfo.InvariantCulture, out var acceptedAmount)
            || acceptedAmount != requiredAmount)
            return FacilitatorErrorCodes.InvalidPaymentRequirements;

        return null;
    }

    private static VerificationResponse Invalid(string reason, string? payer) => new()
    {
        IsValid = false,
        InvalidReason = reason,
        Payer = payer
    };

    private static bool IsBlockHash(string? value) =>
        value != null && value.Length == 64 && value.All(char.IsAsciiHexDigit);

    /// <summary>
    /// Accounts are case-insensitive and "xrb_" is the old spelling of "nano_".
    /// </summary>
    private static string NormalizeAccount(string account)
    {
        account = account.Trim().ToLowerInvariant();
        return account.StartsWith("xrb_", StringComparison.Ordinal) ? "nano_" + account[4..] : account;
    }
}
