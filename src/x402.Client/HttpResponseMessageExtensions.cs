using System.Text;
using System.Text.Json;
using x402.Core.Models.Responses;

namespace x402.Client
{
    public static class HttpResponseMessageExtensions
    {
        public static SettlementResponseHeader? ReadSettlementResponseHeader(this HttpResponseMessage response, JsonSerializerOptions? jsonOptions = null)
        {
            if (!response.Headers.TryGetValues("X-PAYMENT-RESPONSE", out var values))
                return null;

            var base64 = values.FirstOrDefault();
            if (string.IsNullOrEmpty(base64))
                return null;

            try
            {
                jsonOptions ??= JsonSerializerOptions.Web;

                string json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                return JsonSerializer.Deserialize<SettlementResponseHeader>(json, jsonOptions);
            }
            catch
            {
                return null;
            }
        }
    }
}
