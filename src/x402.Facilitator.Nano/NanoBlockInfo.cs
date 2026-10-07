using System.Numerics;

namespace x402.Facilitator.Nano;

/// <summary>
/// What a node reports about one block. Every field comes from the ledger;
/// a field the node did not report is null and fails verification.
/// </summary>
/// <param name="Account">The account that published the block (the payer of a send).</param>
/// <param name="Subtype">send, receive, open, change or epoch.</param>
/// <param name="Destination">The account a send block pays.</param>
/// <param name="AmountRaw">The amount moved by the block, in raw.</param>
/// <param name="Confirmed">True once the network has confirmed the block.</param>
public record NanoBlockInfo(
    string? Account,
    string? Subtype,
    string? Destination,
    BigInteger? AmountRaw,
    bool Confirmed);
