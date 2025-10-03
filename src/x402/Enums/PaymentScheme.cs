using System.Text.Json.Serialization;
using x402.JsonConverters;

namespace x402.Enums
{
    [JsonConverter(typeof(LowercaseEnumConverter<PaymentScheme>))]
    public enum PaymentScheme
    {
        Exact
    }
}
