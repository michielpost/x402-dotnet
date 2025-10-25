using x402.Core.Models;

namespace x402.Core.Interfaces
{
    public interface IAssetInfoProvider
    {
        /// <summary>
        /// Retrieves information about a digital asset associated with the specified contract address.
        /// </summary>
        /// <param name="contractAddress">The contract address of the asset to retrieve information for. Cannot be null or empty.</param>
        /// <returns>An AssetInfo object containing details about the asset if found; otherwise, null.</returns>
        AssetInfo? GetAssetInfo(string contractAddress);

        /// <summary>
        /// Add custom asset info.
        /// </summary>
        /// <param name="assetInfo"></param>
        void AddAssetInfo(AssetInfo assetInfo);

        /// <summary>
        /// Alias that points to a contract address.
        /// </summary>
        /// <param name="alias"></param>
        /// <param name="contractAddress"></param>
        void AddAlias(string alias, string contractAddress);
    }
}
