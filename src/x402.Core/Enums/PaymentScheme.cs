using System.Text.Json.Serialization;
using x402.Core.JsonConverters;

namespace x402.Core.Enums
{
    [JsonConverter(typeof(KebabCaseEnumConverter<PaymentScheme>))]
    public enum PaymentScheme
    {
        /// <summary>
        /// The client pays the exact advertised price.
        /// </summary>
        Exact,

        /// <summary>
        /// The client authorizes a maximum amount; the server settles only what was actually used.
        /// Serialized as "upto". Currently available on EVM networks only.
        /// </summary>
        Upto,

        /// <summary>
        /// The client opens a payment channel with an initial deposit; requests are settled as
        /// signed off-chain vouchers which the server periodically batches into a single on-chain
        /// settlement. Serialized as "batch-settlement". Currently available on EVM networks only.
        /// </summary>
        BatchSettlement
    }
}
