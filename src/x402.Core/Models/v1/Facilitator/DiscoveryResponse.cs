using System.Diagnostics;

namespace x402.Core.Models.v1.Facilitator
{
    public class DiscoveryResponse
    {
        public int X402Version { get; set; } = 1;
        public List<DiscoveryItem> Items { get; set; } = new();
        public Pagination Pagination { get; set; } = new();
    }

    [DebuggerDisplay("{Type} - {Resource}")]
    public class DiscoveryItem
    {
        public string Resource { get; set; } = string.Empty;
        public string Type { get; set; } = "http";
        public int X402Version { get; set; } = 1;
        public List<PaymentRequirements> Accepts { get; set; } = new();
        public required DateTimeOffset LastUpdated { get; set; }
        public object? Metadata { get; set; }
    }

    public class Pagination
    {
        public int Limit { get; set; }
        public int Offset { get; set; }
        public int Total { get; set; }
    }
}
