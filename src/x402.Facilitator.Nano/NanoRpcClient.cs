using System.Globalization;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;

namespace x402.Facilitator.Nano;

/// <summary>
/// Minimal Nano node RPC client: one <c>block_info</c> read per call.
/// </summary>
public class NanoRpcClient : INanoRpcClient
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;

    /// <param name="httpClient">The HTTP client used for the request.</param>
    /// <param name="endpoint">URL of the node's RPC endpoint.</param>
    public NanoRpcClient(HttpClient httpClient, string endpoint)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        _httpClient = httpClient;
        _endpoint = endpoint;
    }

    public async Task<NanoBlockInfo?> GetBlockInfoAsync(string blockHash, CancellationToken cancellationToken = default)
    {
        string body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = JsonContent.Create(new Dictionary<string, string>
                {
                    ["action"] = "block_info",
                    ["json_block"] = "true",
                    ["hash"] = blockHash
                })
            };
            // Some public nodes sit behind a proxy that refuses requests without a User-Agent.
            request.Headers.TryAddWithoutValidation("User-Agent", "x402-dotnet");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new NanoRpcException($"Nano RPC {_endpoint} answered {(int)response.StatusCode}");

            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new NanoRpcException($"Nano RPC {_endpoint} could not be reached", e);
        }

        return ParseBlockInfo(body);
    }

    /// <summary>
    /// Parses a <c>block_info</c> response (requested with <c>json_block</c>).
    /// </summary>
    /// <returns>The block, or null when the node answered "Block not found".</returns>
    /// <exception cref="NanoRpcException">The response is not JSON, or carries any other error.</exception>
    public static NanoBlockInfo? ParseBlockInfo(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new NanoRpcException("Nano RPC answered with invalid JSON", e);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new NanoRpcException("Nano RPC answered with an unexpected JSON value");

            var error = GetString(root, "error");
            if (error != null)
            {
                if (error.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    return null;

                throw new NanoRpcException($"Nano RPC error: {error}");
            }

            JsonElement? contents = root.TryGetProperty("contents", out var c) && c.ValueKind == JsonValueKind.Object ? c : null;
            var contentsType = contents is null ? null : GetString(contents.Value, "type");

            // State blocks report the direction as the top-level "subtype"; legacy blocks
            // carry it as contents.type. "state" itself says nothing about the direction.
            var subtype = GetString(root, "subtype");
            if (subtype == null && contentsType != "state")
                subtype = contentsType;

            string? destination = null;
            if (contents is not null)
                destination = GetString(contents.Value, "link_as_account") ?? GetString(contents.Value, "destination");

            BigInteger? amount = null;
            var amountText = GetString(root, "amount");
            if (amountText != null
                && BigInteger.TryParse(amountText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                amount = parsed;
            }

            return new NanoBlockInfo(
                Account: GetString(root, "block_account"),
                Subtype: subtype,
                Destination: destination,
                AmountRaw: amount,
                Confirmed: IsTrue(root, "confirmed"));
        }
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        return null;
    }

    private static bool IsTrue(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return false;

        // Nodes answer "true" as a string; some proxies turn it into a JSON boolean.
        return value.ValueKind == JsonValueKind.True
            || (value.ValueKind == JsonValueKind.String && value.GetString() == "true");
    }
}
