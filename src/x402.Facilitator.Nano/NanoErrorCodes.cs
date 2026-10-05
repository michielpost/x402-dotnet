namespace x402.Facilitator.Nano;

/// <summary>
/// Nano-specific reasons returned next to the shared <see cref="x402.Core.Models.Facilitator.FacilitatorErrorCodes"/>.
/// </summary>
public static class NanoErrorCodes
{
    /// <summary>The payload carries no block hash, or it is not 64 hexadecimal characters.</summary>
    public static readonly string InvalidBlockHash = "invalid_exact_nano_payload_block_hash";

    /// <summary>The send block pays a different account than the payment requirements name.</summary>
    public static readonly string RecipientMismatch = "invalid_exact_nano_payload_recipient_mismatch";

    /// <summary>The send block does not pay exactly the required raw amount.</summary>
    public static readonly string AmountMismatch = "invalid_exact_nano_payload_amount_mismatch";

    /// <summary>The block hash has already been used to settle a payment.</summary>
    public static readonly string BlockAlreadySettled = "invalid_exact_nano_payload_block_already_settled";
}
