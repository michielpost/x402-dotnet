using System.Net;
using System.Text;
using System.Text.Json;
using x402.Client.Tests.Wallet;
using x402.Client.v1;
using x402.Core.Enums;
using x402.Core.Models.v1;

namespace x402.Client.Tests
{
    public class PaymentRequiredHandlerV1Tests
    {
        private static HttpResponseMessage Build402(params PaymentRequirements[] accepts)
        {
            var body = new PaymentRequiredResponse
            {
                X402Version = 1,
                Accepts = accepts.ToList()
            };
            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return new HttpResponseMessage(HttpStatusCode.PaymentRequired)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        private static PaymentRequirements BuildRequirement(string asset = "0x0000000000000000000000000000000000000000")
        {
            return new PaymentRequirements
            {
                Scheme = PaymentScheme.Exact,
                Network = "base-sepolia",
                MaxAmountRequired = "1000",
                Asset = asset,
                PayTo = "0x1111111111111111111111111111111111111111",
                Resource = "/resource/protected"
            };
        }

        private sealed class QueueMessageHandler : DelegatingHandler
        {
            public readonly List<HttpRequestMessage> SeenRequests = new();
            private readonly Queue<HttpResponseMessage> _responses;

            public QueueMessageHandler(IEnumerable<HttpResponseMessage> responses)
            {
                _responses = new Queue<HttpResponseMessage>(responses);
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                SeenRequests.Add(CloneRequestSync(request));
                if (_responses.Count == 0)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                }
                return Task.FromResult(_responses.Dequeue());
            }

            private static HttpRequestMessage CloneRequestSync(HttpRequestMessage request)
            {
                var clone = new HttpRequestMessage(request.Method, request.RequestUri)
                {
                    Version = request.Version,
                };

                foreach (var header in request.Headers)
                    clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

                if (request.Content != null)
                {
                    var bytes = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    clone.Content = new ByteArrayContent(bytes);
                    foreach (var header in request.Content.Headers)
                        clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                return clone;
            }
        }

        [Test]
        public async Task Adds_X_PAYMENT_Header_On_Retry_When_402_With_Accepts()
        {
            var assetId = "0x036CbD53842c5426634e7929541eC2318f3dCF7e";
            var wallet = new TestWallet(new()
            {
                new() { Asset = assetId, TotalAllowance = 1_000_000_000, MaxPerRequestAllowance = 1_000_000_000 }
            });

            var requirement = BuildRequirement(assetId);
            var inner = new QueueMessageHandler(new[]
            {
                Build402(requirement),
                new HttpResponseMessage(HttpStatusCode.OK)
            });

            var handler = new PaymentRequiredV1Handler(new WalletProvider(wallet), maxRetries: 2)
            {
                InnerHandler = inner
            };
            var client = new HttpClient(handler);

            var response = await client.GetAsync("https://unit.test/resource");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(inner.SeenRequests.Count, Is.EqualTo(2));

            var retryRequest = inner.SeenRequests.Last();
            Assert.That(retryRequest.Headers.TryGetValues("X-PAYMENT", out var values), Is.True);
            var value = values!.Single();
            Assert.That(string.IsNullOrWhiteSpace(value), Is.False);
        }

        [Test]
        public async Task No_Retry_When_Accepts_Empty()
        {
            var wallet = new TestWallet(new());
            wallet.IgnoreAllowances = true; // so wallet would pay if asked

            var inner = new QueueMessageHandler(new[] { Build402() });

            var handler = new PaymentRequiredV1Handler(new WalletProvider(wallet), maxRetries: 3)
            {
                InnerHandler = inner
            };
            var client = new HttpClient(handler);

            var response = await client.GetAsync("https://unit.test/resource");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PaymentRequired));
            Assert.That(inner.SeenRequests.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task Respects_MaxRetries_When_Still_402()
        {
            var assetId = "0x036CbD53842c5426634e7929541eC2318f3dCF7e";
            var wallet = new TestWallet(new()
            {
                new() { Asset = assetId, TotalAllowance = 1_000_000, MaxPerRequestAllowance = 1_000_000 }
            });
            var requirement = BuildRequirement(assetId);

            var inner = new QueueMessageHandler(new[]
            {
                Build402(requirement),
                Build402(requirement),
                new HttpResponseMessage(HttpStatusCode.OK)
            });

            var handler = new PaymentRequiredV1Handler(new WalletProvider(wallet), maxRetries: 1)
            {
                InnerHandler = inner
            };
            var client = new HttpClient(handler);

            var response = await client.GetAsync("https://unit.test/resource");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PaymentRequired));
            Assert.That(inner.SeenRequests.Count, Is.EqualTo(2)); // original + one retry
        }

        [Test]
        public async Task Preserves_Request_Content_On_Retry()
        {
            var assetId = "0x036CbD53842c5426634e7929541eC2318f3dCF7e";
            var wallet = new TestWallet(new()
            {
                new() { Asset = assetId, TotalAllowance = 1_000_000, MaxPerRequestAllowance = 1_000_000 }
            });
            var requirement = BuildRequirement(assetId);

            var inner = new QueueMessageHandler(new[]
            {
                Build402(requirement),
                new HttpResponseMessage(HttpStatusCode.OK)
            });

            var handler = new PaymentRequiredV1Handler(new WalletProvider(wallet), maxRetries: 2)
            {
                InnerHandler = inner
            };
            var client = new HttpClient(handler);

            var content = new StringContent("{\"foo\":\"bar\"}", Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://unit.test/resource", content);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(inner.SeenRequests.Count, Is.EqualTo(2));
            var original = inner.SeenRequests.First();
            var retry = inner.SeenRequests.Last();
            var originalBody = await original.Content!.ReadAsStringAsync();
            var retryBody = await retry.Content!.ReadAsStringAsync();
            Assert.That(retryBody, Is.EqualTo(originalBody));
        }

        // Version 1 specific tests
        [Test]
        public async Task Version1_Uses_X_PAYMENT_Header_On_Retry()
        {
            var assetId = "0x036CbD53842c5426634e7929541eC2318f3dCF7e";
            var wallet = new TestWallet(new()
            {
                new() { Asset = assetId, TotalAllowance = 1_000_000, MaxPerRequestAllowance = 1_000_000 }
            })
            {
                Version = 1
            };
            var requirement = BuildRequirement(assetId);

            var inner = new QueueMessageHandler(new[]
            {
                Build402(requirement),
                new HttpResponseMessage(HttpStatusCode.OK)
            });

            var handler = new PaymentRequiredV1Handler(new WalletProvider(wallet), maxRetries: 2)
            {
                InnerHandler = inner
            };
            var client = new HttpClient(handler);

            var response = await client.GetAsync("https://unit.test/resource");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(inner.SeenRequests.Count, Is.EqualTo(2));

            var retryRequest = inner.SeenRequests.Last();
            Assert.That(retryRequest.Headers.TryGetValues(HttpRequestMessageExtensions.PaymentHeaderV1, out var values), Is.True);
            Assert.That(values!.Single(), Is.Not.Empty);

            // Should NOT have version 2 header
            Assert.That(retryRequest.Headers.Contains(x402.Client.v2.HttpRequestMessageExtensions.PaymentHeaderV2), Is.False);
        }

        [Test]
        public async Task Version1_Parses_JSON_Body()
        {
            var assetId = "0x036CbD53842c5426634e7929541eC2318f3dCF7e";
            var wallet = new TestWallet(new()
            {
                new() { Asset = assetId, TotalAllowance = 1_000_000, MaxPerRequestAllowance = 1_000_000 }
            })
            {
                Version = 1
            };
            var requirement = BuildRequirement(assetId);

            // Response with JSON body (v1 format)
            var body = new PaymentRequiredResponse
            {
                X402Version = 1,
                Accepts = new List<PaymentRequirements> { requirement }
            };
            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var v1Response = new HttpResponseMessage(HttpStatusCode.PaymentRequired)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            var inner = new QueueMessageHandler(new[]
            {
                v1Response,
                new HttpResponseMessage(HttpStatusCode.OK)
            });

            var handler = new PaymentRequiredV1Handler(new WalletProvider(wallet), maxRetries: 2)
            {
                InnerHandler = inner
            };
            var client = new HttpClient(handler);

            var response = await client.GetAsync("https://unit.test/resource");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(inner.SeenRequests.Count, Is.EqualTo(2));
        }

    }
}
