namespace x402.Facilitator.Nano;

/// <summary>
/// Read-only access to one Nano node.
/// </summary>
public interface INanoRpcClient
{
    /// <summary>
    /// Looks up a block by hash.
    /// </summary>
    /// <param name="blockHash">64 hexadecimal characters.</param>
    /// <returns>The block, or null when the node does not know it.</returns>
    /// <exception cref="NanoRpcException">The node could not be reached or gave an unusable answer.</exception>
    Task<NanoBlockInfo?> GetBlockInfoAsync(string blockHash, CancellationToken cancellationToken = default);
}

public class NanoRpcException : Exception
{
    public NanoRpcException(string message) : base(message) { }
    public NanoRpcException(string message, Exception innerException) : base(message, innerException) { }
}
