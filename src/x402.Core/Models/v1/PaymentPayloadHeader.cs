using System.Text.Json;
using x402.Core.Enums;

namespace x402.Core.Models.v1
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
        /// The parsed payload
        /// </summary>
        public required Payload Payload { get; set; }

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
            return Payload?.Authorization?.From;
        }
    }

    public class Payload
    {
        /// <summary>
        /// EIP-712 signature for authorization
        /// </summary>
        public string Signature { get; set; } = string.Empty;

        /// <summary>
        /// EIP-3009 authorization parameter
        /// </summary>
        public required Authorization Authorization { get; set; }

        public string? Resource { get; set; }
    }

    public class Authorization
    {
        /// <summary>
        /// Payer's wallet address
        /// </summary>
        public string From { get; set; } = string.Empty;

        /// <summary>
        /// 	Recipient's wallet address
        /// </summary>
        public string To { get; set; } = string.Empty;

        /// <summary>
        /// Payment amount in atomic units
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// Unix timestamp when authorization becomes valid
        /// </summary>
        public string ValidAfter { get; set; } = string.Empty;

        /// <summary>
        /// Unix timestamp when authorization expires
        /// </summary>
        public string ValidBefore { get; set; } = string.Empty;

        /// <summary>
        /// 32-byte random nonce to prevent replay attacks
        /// </summary>
        public string Nonce { get; set; } = string.Empty;
    }
}
