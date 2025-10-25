using x402.Core.Interfaces;
using x402.Core.Models;

namespace x402.Core
{
    public class AssetInfoProvider : IAssetInfoProvider
    {
        protected Dictionary<string, string> Aliases { get; } = new();
        protected List<AssetInfo> AssetInfos { get; } = new()
        {
            new AssetInfo
            {
                ChainId = 84532,
                Network = "base-sepolia",
                ContractAddress = "0x036CbD53842c5426634e7929541eC2318f3dCF7e", // USDC
                Name = "USDC",
                Version = "2"
            },
            new AssetInfo
            {
                ChainId = 8453,
                Network = "base",
                ContractAddress = "0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913", // USD Coin
                Name = "USD Coin",
                Version = "2"
            },
            new AssetInfo
            {
                ChainId = 43113,
                Network = "avalanche-fuji",
                ContractAddress = "0x5425890298aed601595a70AB815c96711a31Bc65", // USD Coin
                Name = "USD Coin",
                Version = "2"
            },
            new AssetInfo
            {
                ChainId = 43114,
                Network = "avalanche",
                ContractAddress = "0xB97EF9Ef8734C71904D8002F8b6Bc66Dd9c48a6E", // USDC
                Name = "USDC",
                Version = "2"
            },
            new AssetInfo
            {
                ChainId = 103,
                Network = "solana-devnet",
                ContractAddress = "4zMMC9srt5Ri5X14GAgXhaHii3GnPAEERYPJgZJDncDU", // USDC
                Name = "USDC",
                Version = "1"
            },
            new AssetInfo
            {
                ChainId = 101,
                Network = "solana",
                ContractAddress = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v", // USDC
                Name = "USDC",
                Version = "1"
            },
            new AssetInfo
            {
                ChainId = 137,
                Network = "polygon",
                ContractAddress = "0x3c499c542cef5e3811e1192ce70d8cc03d5c3359", // USD Coin
                Name = "USD Coin",
                Version = "1"
            },
            new AssetInfo
            {
                ChainId = 80002,
                Network = "polygon-amoy",
                ContractAddress = "0x41E94Eb019C0762f9Bfcf9Fb1E58725BfB0e7582", // USDC
                Name = "USDC",
                Version = "1"
            }
        };

        public void AddAlias(string alias, string contractAddress)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(alias);
            ArgumentException.ThrowIfNullOrWhiteSpace(contractAddress);

            if (GetAssetInfo(contractAddress) is null)
            {
                throw new ArgumentException(
                    $"Cannot create alias '{alias}' for unknown contract address '{contractAddress}'.",
                    nameof(contractAddress)
                );
            }

            Aliases[alias] = contractAddress;
        }

        public void AddAssetInfo(AssetInfo assetInfo)
        {
            var existing = GetAssetInfo(assetInfo.ContractAddress);
            if (existing != null)
            {
                AssetInfos.Remove(existing);
            }
            AssetInfos.Add(assetInfo);
        }

        public virtual AssetInfo? GetAssetInfo(string contractAddress)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(contractAddress);

            // Resolve alias first, if it exists
            if (Aliases.TryGetValue(contractAddress, out var aliasedContract))
            {
                contractAddress = aliasedContract;
            }

            return AssetInfos.FirstOrDefault(info =>
                string.Equals(info.ContractAddress, contractAddress, StringComparison.OrdinalIgnoreCase));
        }
    }
}
