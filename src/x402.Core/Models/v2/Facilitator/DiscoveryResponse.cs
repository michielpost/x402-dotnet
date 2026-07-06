using System.Diagnostics;
using x402.Core.Enums;

namespace x402.Core.Models.v2.Facilitator
{
    public class DiscoveryResponse
    {
        public int X402Version { get; set; } = 2;
        public List<DiscoveryItem> Items { get; set; } = new();
        public Pagination Pagination { get; set; } = new();
    }

    [DebuggerDisplay("{Type} - {Resource}")]
    public class DiscoveryItem
    {
        public string Resource { get; set; } = string.Empty;
        public string Type { get; set; } = "http";
        public int X402Version { get; set; } = 2;
        public List<DiscoveryPaymentRequirements> Accepts { get; set; } = new();
        public required DateTimeOffset LastUpdated { get; set; }
        public object? Metadata { get; set; }

        /// <summary>
        /// Human-readable description of the resource.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Human-readable name of the service exposing the resource.
        /// </summary>
        public string? ServiceName { get; set; }

        /// <summary>
        /// Topical tags used for facilitator-side filtering and search.
        /// </summary>
        public List<string>? Tags { get; set; }

        /// <summary>
        /// Absolute http(s) URL to an icon representing the service.
        /// </summary>
        public string? IconUrl { get; set; }

        /// <summary>
        /// Protocol extensions declared by the resource (e.g. "bazaar").
        /// </summary>
        public Dictionary<string, ExtensionData>? Extensions { get; set; }

        /// <summary>
        /// Usage/quality statistics reported by the facilitator.
        /// </summary>
        public DiscoveryQuality? Quality { get; set; }
    }

    /// <summary>
    /// Usage statistics the facilitator tracks for a discovered resource.
    /// </summary>
    public class DiscoveryQuality
    {
        /// <summary>
        /// Total number of calls in the last 30 days.
        /// </summary>
        public long? L30DaysTotalCalls { get; set; }

        /// <summary>
        /// Number of unique payers in the last 30 days.
        /// </summary>
        public long? L30DaysUniquePayers { get; set; }

        /// <summary>
        /// Timestamp of the most recent call to the resource.
        /// </summary>
        public DateTimeOffset? LastCalledAt { get; set; }
    }

    public class Pagination
    {
        public int Limit { get; set; }
        public int Offset { get; set; }
        public int Total { get; set; }
    }

    public class DiscoveryPaymentRequirements
    {
        /// <summary>
        /// The payment scheme (e.g., "exact").
        /// </summary>
        public PaymentScheme Scheme { get; set; }

        /// <summary>
        /// The network identifier (e.g., "eip155:84532").
        /// </summary>
        public required string Network { get; set; }

        /// <summary>
        /// The maximum amount required in atomic units.
        /// Used for v1 compatibility.
        /// </summary>
        public string? MaxAmountRequired { get; set; }

        /// <summary>
        /// The maximum amount required in atomic units.
        /// Used for v2
        /// </summary>
        public string? Amount { get; set; }

        /// <summary>
        /// The asset contract address
        /// </summary>
        public required string Asset { get; set; }

        /// <summary>
        /// The pay-to wallet address.
        /// </summary>
        public required string PayTo { get; set; }

        /// <summary>
        /// The maximum timeout in seconds.
        /// </summary>
        public int MaxTimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// Scheme-specific additional information
        /// </summary>
        public PaymentRequirementsExtra? Extra { get; set; }
    }
}
