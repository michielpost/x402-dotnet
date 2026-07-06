namespace x402.Core.Models.v2.Facilitator
{
    /// <summary>
    /// Query parameters for searching discovered x402 resources (GET discovery/search).
    /// All filters are optional and combined with AND semantics.
    /// </summary>
    public class DiscoverySearchRequest
    {
        /// <summary>
        /// Full-text or semantic search query to find matching resources.
        /// </summary>
        public string? Query { get; set; }

        /// <summary>
        /// Filter by network in CAIP-2 format (e.g., "eip155:8453") or legacy name (e.g., "base").
        /// </summary>
        public string? Network { get; set; }

        /// <summary>
        /// Filter by asset address (case-insensitive).
        /// </summary>
        public string? Asset { get; set; }

        /// <summary>
        /// Filter by payment scheme (e.g., "exact").
        /// </summary>
        public string? Scheme { get; set; }

        /// <summary>
        /// Filter by the merchant's payment address.
        /// </summary>
        public string? PayTo { get; set; }

        /// <summary>
        /// Filter to resources whose URL contains this value (case-insensitive substring match).
        /// </summary>
        public string? UrlSubstring { get; set; }

        /// <summary>
        /// Filter to resources with a USD price at or below this value (e.g., "1.00").
        /// </summary>
        public string? MaxUsdPrice { get; set; }

        /// <summary>
        /// Filter to resources that support the specified protocol extensions (e.g., "bazaar").
        /// </summary>
        public List<string>? Extensions { get; set; }

        /// <summary>
        /// Maximum number of resources to return (1-20). Defaults to 20 when null.
        /// </summary>
        public int? Limit { get; set; }
    }

    /// <summary>
    /// Response from a search for x402 resources.
    /// </summary>
    public class DiscoverySearchResponse
    {
        public int X402Version { get; set; } = 2;

        /// <summary>
        /// List of x402 resources matching the search query and filters.
        /// </summary>
        public List<DiscoveryItem> Resources { get; set; } = new();

        /// <summary>
        /// Whether the result set was truncated because there were more results than the requested limit.
        /// </summary>
        public bool PartialResults { get; set; }

        /// <summary>
        /// The search method used to retrieve the results (e.g., "text", "vector", "hybrid").
        /// </summary>
        public string? SearchMethod { get; set; }
    }
}
