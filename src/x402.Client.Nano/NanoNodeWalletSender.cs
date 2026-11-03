using System.Globalization;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;

namespace x402.Client.Nano
{
    /// <summary>
    /// Sends through the wallet of a Nano node you run, with the node RPC <c>send</c> action,
    /// then waits until <c>block_info</c> reports the block as confirmed. The key stays in the
    /// node; this class never sees it. The node must have <c>enable_control</c> set, so point
    /// it only at a node you control, never at a public one.
    /// </summary>
    public class NanoNodeWalletSender : INanoSender
    {
        private readonly HttpClient httpClient;
        private readonly string endpoint;
        private readonly string walletId;
        private readonly string source;

        /// <summary>How long to wait for the network to confirm the block.</summary>
        public TimeSpan ConfirmationTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Delay between two <c>block_info</c> reads while waiting for confirmation.</summary>
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

        /// <param name="httpClient">The HTTP client used for the RPC calls.</param>
        /// <param name="endpoint">URL of your node's RPC endpoint.</param>
        /// <param name="walletId">Id of the node wallet that holds <paramref name="source"/>.</param>
        /// <param name="source">The paying account, <c>nano_...</c>.</param>
        public NanoNodeWalletSender(HttpClient httpClient, string endpoint, string walletId, string source)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
            ArgumentException.ThrowIfNullOrWhiteSpace(walletId);
            if (!NanoWallet.IsAccount(source))
                throw new ArgumentException($"'{source}' is not a Nano account.", nameof(source));

            this.httpClient = httpClient;
            this.endpoint = endpoint;
            this.walletId = walletId;
            this.source = source.Trim().ToLowerInvariant();
        }

        public async Task<string> SendAsync(string destination, BigInteger amountRaw, string id, CancellationToken cancellationToken = default)
        {
            if (!NanoWallet.IsAccount(destination))
                throw new ArgumentException($"'{destination}' is not a Nano account.", nameof(destination));
            if (amountRaw <= BigInteger.Zero)
                throw new ArgumentOutOfRangeException(nameof(amountRaw), "The amount must be positive.");
            ArgumentException.ThrowIfNullOrWhiteSpace(id);

            // The node remembers "id": sending the same id again returns the first block
            // instead of paying twice.
            using var sent = await CallAsync(new Dictionary<string, string>
            {
                ["action"] = "send",
                ["wallet"] = walletId,
                ["source"] = source,
                ["destination"] = destination.Trim().ToLowerInvariant(),
                ["amount"] = amountRaw.ToString(CultureInfo.InvariantCulture),
                ["id"] = id
            }, cancellationToken).ConfigureAwait(false);

            if (!sent.RootElement.TryGetProperty("block", out var blockElement)
                || blockElement.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("Nano RPC send did not return a block hash.");

            var blockHash = blockElement.GetString()!;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConfirmationTimeout);
            try
            {
                while (true)
                {
                    if (await IsConfirmedAsync(blockHash, timeout.Token).ConfigureAwait(false))
                        return blockHash;

                    await Task.Delay(PollInterval, timeout.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Nano block {blockHash} was published but not confirmed within {ConfirmationTimeout}.");
            }
        }

        private async Task<bool> IsConfirmedAsync(string blockHash, CancellationToken cancellationToken)
        {
            using var info = await CallAsync(new Dictionary<string, string>
            {
                ["action"] = "block_info",
                ["json_block"] = "true",
                ["hash"] = blockHash
            }, cancellationToken, treatNotFoundAsNull: true).ConfigureAwait(false);

            // Right after "send" the node may not know the block yet: keep polling.
            if (info == null)
                return false;

            if (!info.RootElement.TryGetProperty("confirmed", out var confirmed))
                return false;

            // Nodes answer "true" as a string; some proxies turn it into a JSON boolean.
            return confirmed.ValueKind == JsonValueKind.True
                || (confirmed.ValueKind == JsonValueKind.String && confirmed.GetString() == "true");
        }

        private async Task<JsonDocument> CallAsync(Dictionary<string, string> body, CancellationToken cancellationToken)
            => (await CallAsync(body, cancellationToken, treatNotFoundAsNull: false).ConfigureAwait(false))!;

        private async Task<JsonDocument?> CallAsync(Dictionary<string, string> body, CancellationToken cancellationToken, bool treatNotFoundAsNull)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.TryAddWithoutValidation("User-Agent", "x402-dotnet");

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Nano RPC {endpoint} answered {(int)response.StatusCode}.");

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new InvalidOperationException("Nano RPC answered with an unexpected JSON value.");
            }

            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var message = error.ToString();
                document.Dispose();
                if (treatNotFoundAsNull && message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    return null;

                throw new InvalidOperationException($"Nano RPC {body["action"]} failed: {message}");
            }

            return document;
        }
    }
}
