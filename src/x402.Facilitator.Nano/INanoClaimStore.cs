using System.Collections.Concurrent;

namespace x402.Facilitator.Nano;

/// <summary>
/// Remembers which send blocks have already paid for something, so one block
/// settles one payment.
/// </summary>
public interface INanoClaimStore
{
    /// <summary>
    /// Atomically marks a block hash as used.
    /// </summary>
    /// <returns>True for the first claim of this hash, false when it was already claimed.</returns>
    bool TryClaim(string blockHash);

    /// <summary>
    /// Whether the block hash has already been claimed.
    /// </summary>
    bool IsClaimed(string blockHash);
}

/// <summary>
/// Keeps claims in memory. Claims are lost on restart and are not shared between
/// instances; register a persistent <see cref="INanoClaimStore"/> when that matters.
/// </summary>
public class InMemoryNanoClaimStore : INanoClaimStore
{
    private readonly ConcurrentDictionary<string, byte> _claimed = new(StringComparer.OrdinalIgnoreCase);

    public bool TryClaim(string blockHash) => _claimed.TryAdd(blockHash, 0);

    public bool IsClaimed(string blockHash) => _claimed.ContainsKey(blockHash);
}
