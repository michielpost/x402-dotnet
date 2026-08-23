using System.Numerics;
using System.Text.Json;
using Casper.Network.SDK.Types;
using x402.Core.Enums;
using x402.Core.Models.v2;

namespace x402.Client.Casper.Tests
{
    public class CasperWalletTests
    {
        private const string PayTo = "0019b7f61e7ccac947ba0d0a4b76947ad87200a5fd1805c09d9ee67c1bc82ed920";
        private const string TestnetAsset = CasperNetworks.TestnetWCsprPackageHash;

        private static PaymentRequirements BuildRequirement(string network = CasperNetworks.Testnet)
        {
            return new PaymentRequirements
            {
                Scheme = PaymentScheme.Exact,
                Network = network,
                Amount = "1000000000",
                Asset = TestnetAsset,
                PayTo = PayTo,
                Extra = new PaymentRequirementsExtra
                {
                    Name = CasperNetworks.WCsprName,
                    Version = CasperNetworks.WCsprVersion
                }
            };
        }

        private static CasperWallet BuildWallet(KeyAlgo algorithm = KeyAlgo.ED25519, string network = CasperNetworks.Testnet)
        {
            return new CasperWallet(KeyPair.CreateNew(algorithm), network) { IgnoreAllowances = true };
        }

        [Test]
        public async Task CreateHeader_BuildsHeaderWithExpectedMappings()
        {
            var requirement = BuildRequirement();
            var wallet = BuildWallet();

            var selected = await wallet.SelectPaymentAsync(new PaymentRequiredResponse
            {
                Accepts = [requirement],
                Resource = new ResourceInfo { Url = "/resource/protected" }
            }, CancellationToken.None);

            var header = await wallet.CreateHeaderAsync(selected!, CancellationToken.None);

            Assert.That(selected, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(header.X402Version, Is.EqualTo(2));
                Assert.That(header.Accepted.Network, Is.EqualTo(requirement.Network));
                Assert.That(header.Accepted.Scheme, Is.EqualTo(requirement.Scheme));
                Assert.That(header.Payload.Authorization.From, Is.EqualTo(wallet.OwnerAddress));
                Assert.That(header.Payload.Authorization.To, Is.EqualTo(PayTo));
                Assert.That(header.Payload.Authorization.Value, Is.EqualTo("1000000000"));
                Assert.That(header.Payload.PublicKey, Is.EqualTo(wallet.PublicKeyHex));
            });
        }

        [Test]
        public async Task CreateHeader_SignatureIsAlgorithmTagged65Bytes()
        {
            var wallet = BuildWallet();

            var header = await wallet.CreateHeaderAsync(BuildRequirement(), CancellationToken.None);
            var signature = header.Payload.Signature;

            Assert.Multiple(() =>
            {
                // 65 bytes: one algorithm tag byte plus the 64 byte raw signature.
                Assert.That(signature, Has.Length.EqualTo(130));
                Assert.That(signature, Does.StartWith("01"));
                Assert.That(signature, Is.EqualTo(signature.ToLowerInvariant()));
                Assert.DoesNotThrow(() => Convert.FromHexString(signature));
            });
        }

        [Test]
        public async Task CreateHeader_Secp256k1SignatureCarriesItsOwnTag()
        {
            var wallet = BuildWallet(KeyAlgo.SECP256K1);

            var header = await wallet.CreateHeaderAsync(BuildRequirement(), CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(header.Payload.Signature, Does.StartWith("02"));
                Assert.That(header.Payload.Signature, Has.Length.EqualTo(130));
                Assert.That(header.Payload.PublicKey, Does.StartWith("02"));
            });
        }

        [Test]
        public async Task CreateHeader_SignsTheCasperEip712Digest()
        {
            // The signature must verify against the digest the facilitator recomputes,
            // which is what makes the payment settle rather than merely look well formed.
            var keyPair = KeyPair.CreateNew(KeyAlgo.ED25519);
            var wallet = new CasperWallet(keyPair, CasperNetworks.Testnet) { IgnoreAllowances = true };
            var requirement = BuildRequirement();

            var header = await wallet.CreateHeaderAsync(requirement, CancellationToken.None);
            var authorization = header.Payload.Authorization;

            var domainSeparator = CasperEip712.HashDomain(
                CasperNetworks.WCsprName,
                CasperNetworks.WCsprVersion,
                CasperNetworks.Testnet,
                Convert.FromHexString(TestnetAsset));

            var structHash = CasperEip712.HashTransferWithAuthorization(
                Convert.FromHexString(authorization.From),
                Convert.FromHexString(authorization.To),
                BigInteger.Parse(authorization.Value),
                long.Parse(authorization.ValidAfter),
                long.Parse(authorization.ValidBefore),
                Convert.FromHexString(authorization.Nonce));

            var digest = CasperEip712.HashTypedData(domainSeparator, structHash);

            // Strip the algorithm tag byte to recover the raw signature.
            var rawSignature = Convert.FromHexString(header.Payload.Signature)[1..];

            Assert.That(keyPair.PublicKey.VerifySignature(digest, rawSignature), Is.True);
        }

        [Test]
        public async Task CreateHeader_ExternalSignerProducesTheSamePayer()
        {
            var keyPair = KeyPair.CreateNew(KeyAlgo.ED25519);
            var embedded = new CasperWallet(keyPair, CasperNetworks.Testnet);
            var external = new CasperWallet(
                digest => Task.FromResult(keyPair.Sign(digest)),
                keyPair.PublicKey.ToString()!,
                CasperNetworks.Testnet);

            var header = await external.CreateHeaderAsync(BuildRequirement(), CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(external.OwnerAddress, Is.EqualTo(embedded.OwnerAddress));
                Assert.That(header.Payload.Authorization.From, Is.EqualTo(embedded.OwnerAddress));
                Assert.That(header.Payload.Signature, Does.StartWith("01"));
            });
        }

        [Test]
        public void CreateHeader_RejectsSignersThatReturnMalformedSignatures()
        {
            var keyPair = KeyPair.CreateNew(KeyAlgo.ED25519);
            var wallet = new CasperWallet(
                _ => Task.FromResult(new byte[65]),
                keyPair.PublicKey.ToString()!,
                CasperNetworks.Testnet);

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await wallet.CreateHeaderAsync(BuildRequirement(), CancellationToken.None));
        }

        [Test]
        public void OwnerAddress_IsTheAccountHashBehindItsTagByte()
        {
            var keyPair = KeyPair.CreateNew(KeyAlgo.ED25519);
            var wallet = new CasperWallet(keyPair, CasperNetworks.Testnet);

            var expected = "00" + keyPair.PublicKey.GetAccountHash()["account-hash-".Length..].ToLowerInvariant();

            Assert.Multiple(() =>
            {
                Assert.That(wallet.OwnerAddress, Is.EqualTo(expected));
                Assert.That(wallet.OwnerAddress, Has.Length.EqualTo(66));
                // The payer address is the account hash, never the public key.
                Assert.That(wallet.OwnerAddress, Is.Not.EqualTo(wallet.PublicKeyHex));
            });
        }

        [Test]
        public async Task CreateHeader_GeneratesUniqueNonces()
        {
            var wallet = BuildWallet();
            var requirement = BuildRequirement();

            var first = await wallet.CreateHeaderAsync(requirement, CancellationToken.None);
            var second = await wallet.CreateHeaderAsync(requirement, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(first.Payload.Authorization.Nonce, Has.Length.EqualTo(64));
                Assert.That(first.Payload.Authorization.Nonce, Is.Not.EqualTo(second.Payload.Authorization.Nonce));
                Assert.That(first.Payload.Signature, Is.Not.EqualTo(second.Payload.Signature));
            });
        }

        [Test]
        public async Task CreateHeader_UsesTheServerTimeoutForTheValidityWindow()
        {
            var wallet = BuildWallet();
            var requirement = BuildRequirement();
            requirement.MaxTimeoutSeconds = 120;

            var before = DateTimeOffset.UtcNow;
            var header = await wallet.CreateHeaderAsync(requirement, CancellationToken.None);
            var after = DateTimeOffset.UtcNow;

            var validAfter = long.Parse(header.Payload.Authorization.ValidAfter);
            var validBefore = long.Parse(header.Payload.Authorization.ValidBefore);

            Assert.Multiple(() =>
            {
                // Backdated by the clock skew allowance so a slightly fast payer clock
                // does not produce an authorization the facilitator considers premature.
                Assert.That(validAfter, Is.InRange(before.AddMinutes(-10).ToUnixTimeSeconds() - 5, before.AddMinutes(-10).ToUnixTimeSeconds() + 5));
                Assert.That(validBefore, Is.InRange(after.AddSeconds(120).ToUnixTimeSeconds() - 5, after.AddSeconds(120).ToUnixTimeSeconds() + 5));
                Assert.That(validBefore, Is.GreaterThan(validAfter));
            });
        }

        [Test]
        public async Task CreateHeader_FallsBackToTheWalletWindowWithoutAServerTimeout()
        {
            var wallet = BuildWallet();
            wallet.AddValidBeforeFromNow = TimeSpan.FromMinutes(5);

            var requirement = BuildRequirement();
            requirement.MaxTimeoutSeconds = 0;

            var expected = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
            var header = await wallet.CreateHeaderAsync(requirement, CancellationToken.None);

            Assert.That(long.Parse(header.Payload.Authorization.ValidBefore), Is.InRange(expected - 5, expected + 5));
        }

        [Test]
        public async Task CreateHeader_SerializesToACamelCasePaymentHeader()
        {
            var wallet = BuildWallet();

            var header = await wallet.CreateHeaderAsync(BuildRequirement(), CancellationToken.None);
            var json = JsonDocument.Parse(
                System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(header.ToBase64Header()))).RootElement;

            var payload = json.GetProperty("payload");
            var authorization = payload.GetProperty("authorization");

            Assert.Multiple(() =>
            {
                Assert.That(json.GetProperty("x402Version").GetInt32(), Is.EqualTo(2));
                Assert.That(payload.GetProperty("publicKey").GetString(), Is.EqualTo(wallet.PublicKeyHex));
                Assert.That(authorization.GetProperty("validAfter").GetString(), Is.Not.Empty);
                Assert.That(authorization.GetProperty("nonce").GetString(), Has.Length.EqualTo(64));
                Assert.That(json.GetProperty("accepted").GetProperty("network").GetString(), Is.EqualTo(CasperNetworks.Testnet));
            });
        }

        [Test]
        public async Task ToBase64Header_OmitsPublicKeyForNonCasperPayloads()
        {
            // The shared payload model is used by every rail, so the Casper only field
            // must not appear in an EVM or Solana payment header.
            var header = new PaymentPayloadHeader
            {
                X402Version = 2,
                Accepted = BuildRequirement(),
                Payload = new Payload { Signature = "0xabc", Authorization = new Authorization() }
            };

            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(header.ToBase64Header()));

            Assert.That(json, Does.Not.Contain("publicKey"));
            await Task.CompletedTask;
        }

        [Test]
        public async Task SelectPayment_IgnoresNonCasperRequirements()
        {
            var wallet = BuildWallet();

            var selected = await wallet.SelectPaymentAsync(new PaymentRequiredResponse
            {
                Accepts =
                [
                    new PaymentRequirements
                    {
                        Scheme = PaymentScheme.Exact,
                        Network = "eip155:8453",
                        Amount = "1000",
                        Asset = "0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913",
                        PayTo = "0x7D95514aEd9f13Aa89C8e5Ed9c29D08E8E9BfA37"
                    },
                    BuildRequirement()
                ],
                Resource = new ResourceInfo { Url = "/resource/protected" }
            }, CancellationToken.None);

            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.Network, Is.EqualTo(CasperNetworks.Testnet));
        }

        [Test]
        public void CreateHeader_RejectsNonCasperNetworks()
        {
            var wallet = BuildWallet(KeyAlgo.ED25519, "eip155:8453");
            var requirement = BuildRequirement("eip155:8453");

            var exception = Assert.ThrowsAsync<ArgumentException>(
                async () => await wallet.CreateHeaderAsync(requirement, CancellationToken.None));

            Assert.That(exception!.Message, Does.Contain("not a Casper network"));
        }

        [Test]
        public void CreateHeader_RequiresTheTokenNameAndVersion()
        {
            var wallet = BuildWallet();

            var withoutName = BuildRequirement();
            withoutName.Extra = new PaymentRequirementsExtra { Version = "1" };

            var withoutVersion = BuildRequirement();
            withoutVersion.Extra = new PaymentRequirementsExtra { Name = CasperNetworks.WCsprName };

            var missingExtra = BuildRequirement();
            missingExtra.Extra = null;

            Assert.Multiple(() =>
            {
                Assert.ThrowsAsync<ArgumentException>(async () => await wallet.CreateHeaderAsync(withoutName, CancellationToken.None));
                Assert.ThrowsAsync<ArgumentException>(async () => await wallet.CreateHeaderAsync(withoutVersion, CancellationToken.None));
                Assert.ThrowsAsync<ArgumentException>(async () => await wallet.CreateHeaderAsync(missingExtra, CancellationToken.None));
            });
        }

        [Test]
        public void CreateHeader_RejectsMalformedAmounts()
        {
            var wallet = BuildWallet();

            var negative = BuildRequirement();
            negative.Amount = "-1";

            var fractional = BuildRequirement();
            fractional.Amount = "1.5";

            Assert.Multiple(() =>
            {
                Assert.ThrowsAsync<ArgumentException>(async () => await wallet.CreateHeaderAsync(negative, CancellationToken.None));
                Assert.ThrowsAsync<ArgumentException>(async () => await wallet.CreateHeaderAsync(fractional, CancellationToken.None));
            });
        }

        [Test]
        public void ParseContractPackageHash_AcceptsThePrefixedForms()
        {
            var expected = Convert.FromHexString(TestnetAsset);

            Assert.Multiple(() =>
            {
                Assert.That(CasperWallet.ParseContractPackageHash(TestnetAsset), Is.EqualTo(expected));
                Assert.That(CasperWallet.ParseContractPackageHash("hash-" + TestnetAsset), Is.EqualTo(expected));
                Assert.That(CasperWallet.ParseContractPackageHash("0x" + TestnetAsset), Is.EqualTo(expected));
                Assert.That(CasperWallet.ParseContractPackageHash(TestnetAsset.ToUpperInvariant()), Is.EqualTo(expected));
            });
        }

        [Test]
        public void ParseContractPackageHash_RejectsMalformedAssets()
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentException>(() => CasperWallet.ParseContractPackageHash("0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913"));
                Assert.Throws<ArgumentException>(() => CasperWallet.ParseContractPackageHash(TestnetAsset[..62]));
                Assert.Throws<ArgumentException>(() => CasperWallet.ParseContractPackageHash(new string('z', 64)));
            });
        }

        [Test]
        public void ParseAuthorizationAddress_AcceptsTheAccountHashPrefixedForm()
        {
            var expected = Convert.FromHexString(PayTo);

            Assert.That(
                CasperWallet.ParseAuthorizationAddress("account-hash-" + PayTo[2..], "payTo"),
                Is.EqualTo(expected));
        }

        [Test]
        public void ParseAuthorizationAddress_ExplainsAPublicKeyMistake()
        {
            // A public key is 33 bytes too, but tagged 01 or 02, so it decodes cleanly
            // while producing a digest the facilitator will not accept.
            var publicKey = KeyPair.CreateNew(KeyAlgo.ED25519).PublicKey.ToString()!;

            var exception = Assert.Throws<ArgumentException>(
                () => CasperWallet.ParseAuthorizationAddress(publicKey[..64], "payTo"));

            Assert.That(exception!.Message, Does.Contain("account hash"));
        }

        [Test]
        public void IsCasperNetwork_MatchesOnlyCasperNetworks()
        {
            Assert.Multiple(() =>
            {
                Assert.That(CasperNetworks.IsCasperNetwork(CasperNetworks.Mainnet), Is.True);
                Assert.That(CasperNetworks.IsCasperNetwork(CasperNetworks.Testnet), Is.True);
                Assert.That(CasperNetworks.IsCasperNetwork("eip155:8453"), Is.False);
                Assert.That(CasperNetworks.IsCasperNetwork("solana:5eykt4UsFv8P8NJdTREpY1vzqKqZKvdp"), Is.False);
                Assert.That(CasperNetworks.IsCasperNetwork(null), Is.False);
                Assert.That(CasperNetworks.IsCasperNetwork("  "), Is.False);
            });
        }
    }
}
