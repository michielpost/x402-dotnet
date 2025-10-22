using x402.Core.Models;

namespace x402.Core.Interfaces
{
    public interface ITokenInfoProvider
    {
        TokenInfo? GetTokenInfo(string contractAddress);
    }
}
