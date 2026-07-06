using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace x402.Core.JsonConverters
{
    /// <summary>
    /// Serializes enum values as lowercase kebab-case (e.g. BatchSettlement => "batch-settlement").
    /// Reading accepts values with or without hyphens, case-insensitive.
    /// </summary>
    public class KebabCaseEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.GetString();
            if (value is null)
                throw new JsonException("Expected string for enum value.");

            // Strip hyphens so "batch-settlement" parses as "BatchSettlement"
            var normalized = value.Replace("-", string.Empty);
            if (Enum.TryParse<TEnum>(normalized, ignoreCase: true, out var result))
            {
                return result;
            }

            throw new JsonException($"Unable to convert \"{value}\" to {typeof(TEnum)}.");
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(ToKebabCase(value.ToString()));
        }

        internal static string ToKebabCase(string value)
        {
            var sb = new StringBuilder(value.Length + 4);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsUpper(c) && i > 0)
                {
                    sb.Append('-');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }
}
