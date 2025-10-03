using System.Text.Json;
using System.Text.Json.Serialization;
using x402.Enums;

namespace x402.Models
{
    /// <summary>
    /// Payload extracted from the X-PAYMENT header.
    /// </summary>
    public class PaymentPayloadHeader
    {
        /// <summary>
        /// The X402 version.
        /// </summary>
        public int X402Version { get; set; }

        /// <summary>
        /// The payment scheme (e.g., "exact").
        /// </summary>
        public PaymentScheme Scheme { get; set; }

        /// <summary>
        /// The network identifier (e.g., "base-sepolia").
        /// </summary>
        public string? Network { get; set; }

        /// <summary>
        /// The parsed payload as a dictionary.
        /// </summary>
        public Dictionary<string, object> Payload { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Parses the payment payload from the base64-encoded header.
        /// </summary>
        /// <param name="header">The X-PAYMENT header value.</param>
        /// <returns>The parsed PaymentPayload.</returns>
        /// <exception cref="ArgumentException">If the header is malformed.</exception>
        public static PaymentPayloadHeader FromHeader(string header)
        {
            try
            {
                byte[] decodedBytes = Convert.FromBase64String(header);
                string jsonString = System.Text.Encoding.UTF8.GetString(decodedBytes);

                JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
                var payload = JsonSerializer.Deserialize<PaymentPayloadHeader>(jsonString, jsonOptions);
                if (payload == null)
                {
                    throw new ArgumentException("Invalid JSON in header");
                }
                return payload;
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Malformed X-PAYMENT header", ex);
            }
        }

        /// <summary>
        /// Extract the payer wallet address from payment payload.
        /// </summary>
        public string? ExtractPayerFromPayload()
        {
            try
            {
                // Convert the generic payload dict to a typed ExactSchemePayload
                string payloadJson = JsonSerializer.Serialize(Payload);

                JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
                ExactSchemePayload? exactPayload = JsonSerializer.Deserialize<ExactSchemePayload>(payloadJson, jsonOptions);
                return exactPayload?.Authorization?.From;
            }
            catch (Exception)
            {
                // If conversion fails, fall back to manual extraction for compatibility
                try
                {
                    if (Payload.TryGetValue("authorization", out var authorizationObj) &&
                        authorizationObj is Dictionary<string, object> authorization)
                    {
                        if (authorization.TryGetValue("from", out var fromObj) &&
                            fromObj is string from)
                        {
                            return from;
                        }
                    }
                }
                catch
                {
                    // Ignore any extraction errors
                }
                return null;
            }
        }
    }
}
