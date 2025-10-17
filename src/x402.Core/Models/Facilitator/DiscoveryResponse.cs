namespace x402.Core.Models.Facilitator
{
    public class DiscoveryResponse
    {
        public int X402Version { get; set; }
        public List<Item> Items { get; set; } = new();
        public Pagination Pagination { get; set; } = new();
    }

    public class Item
    {
        public string Resource { get; set; } = string.Empty;
        public string Type { get; set; } = "http";
        public int X402Version { get; set; } = 1;
        public List<PaymentRequirements> Accepts { get; set; } = new();
        public required long LastUpdated { get; set; }
        public Metadata? Metadata { get; set; }
    }

    public class Metadata
    {
        public string? Category { get; set; }
        public string? Provider { get; set; }
    }

    public class Pagination
    {
        public int Limit { get; set; }
        public int Offset { get; set; }
        public int Total { get; set; }
    }
}
