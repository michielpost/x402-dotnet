namespace x402.Core.Models.v2
{
    /// <summary>
    /// Response for 402 Payment Required.
    /// </summary>
    public class PaymentRequiredResponse
    {
        /// <summary>
        /// The X402 version.
        /// </summary>
        public int X402Version { get; set; }

        public required ResourceInfo Resource { get; set; }

        /// <summary>
        /// List of accepted payment requirements.
        /// </summary>
        public List<PaymentRequirements> Accepts { get; set; } = new List<PaymentRequirements>();

        /// <summary>
        /// Error message, if any.
        /// </summary>
        public string? Error { get; set; }

        public Dictionary<string, ExtensionData>? Extensions { get; set; }
    }

    public class ResourceInfo
    {
        /// <summary>
        /// The resource path.
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable description of the resource
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the website URL associated with the entity.
        /// </summary>
        public string Website { get; set; } = string.Empty;

        /// <summary>
        /// The MIME type of the resource.
        /// </summary>
        public string MimeType { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable name of the service, shown by discovery layers (e.g. the Bazaar).
        /// Max 32 printable ASCII characters; facilitators silently drop invalid values.
        /// </summary>
        public string? ServiceName { get; set; }

        /// <summary>
        /// Topical tags used for facilitator-side filtering and search.
        /// Max 5 tags of 32 printable ASCII characters each.
        /// </summary>
        public List<string>? Tags { get; set; }

        /// <summary>
        /// Icon shown by discovery layers. Must be an absolute http(s) URL
        /// (no IP literals or loopback hostnames), max 2048 characters.
        /// </summary>
        public string? IconUrl { get; set; }
    }

    public class ExtensionData
    {
        public object? Info { get; set; }
        public object? Schema { get; set; }
    }
}
