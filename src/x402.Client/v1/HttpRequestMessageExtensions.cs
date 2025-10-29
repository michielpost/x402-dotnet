using System.Text;
using System.Text.Json;
using x402.Core.Models.v1;

namespace x402.Client.v1
{
    public static class HttpRequestMessageExtensions
    {
        public const string PaymentHeaderV1 = "X-PAYMENT";

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public static void AddPaymentHeader(this HttpRequestMessage request, PaymentPayloadHeader header, int version = 1)
        {
            var headerJson = JsonSerializer.Serialize(header, JsonOptions);
            var base64header = Convert.ToBase64String(Encoding.UTF8.GetBytes(headerJson));

            // Use the appropriate header based on X402 version
            request.Headers.Add(PaymentHeaderV1, base64header);
        }
    }
}
