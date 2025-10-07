using System.Text.Json.Serialization;
using x402.JsonConverters;

namespace x402.Enums
{
    [JsonConverter(typeof(LowercaseEnumConverter<SettlementMode>))]
    public enum SettlementMode
    {
        /// <summary>
        /// Optimistic settlement mode: request is handled immediately after verify succeeded, without waiting for settlement confirmation.
        /// </summary>
        Optimistic,

        /// <summary>
        /// Pessimistic settlement mode: request is handled only after settlement is confirmed.
        /// </summary>
        Pessimistic
    }
}


