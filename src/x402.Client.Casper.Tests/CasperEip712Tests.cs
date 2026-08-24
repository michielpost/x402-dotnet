using System.Numerics;

namespace x402.Client.Casper.Tests
{
    /// <summary>
    /// Verifies the Casper EIP-712 digest against the reference test vector published
    /// by the Casper x402 mechanism, so a change in the hashing rules cannot silently
    /// start producing signatures the facilitator rejects.
    /// </summary>
    public class CasperEip712Tests
    {
        private const string ReferenceHash = "aabbccddeeff0011223344556677889900aabbccddeeff001122334455667788";

        [Test]
        public void HashTypedData_MatchesReferenceVector()
        {
            // Reference vector: TransferWithAuthorization digest for the TestToken domain.
            var contractPackageHash = Convert.FromHexString(ReferenceHash);
            var nonce = Convert.FromHexString(ReferenceHash);

            var from = Convert.FromHexString("01" + ReferenceHash);
            var to = Convert.FromHexString("00" + ReferenceHash);

            var domainSeparator = CasperEip712.HashDomain("TestToken", "1", "casper-test", contractPackageHash);
            var structHash = CasperEip712.HashTransferWithAuthorization(
                from,
                to,
                new BigInteger(1000000),
                1700000000,
                1700001000,
                nonce);

            var digest = CasperEip712.HashTypedData(domainSeparator, structHash);

            Assert.That(
                Convert.ToHexString(digest).ToLowerInvariant(),
                Is.EqualTo("f49af32a160ef6078d23bd28c15e0e8d6d29e58f4cb88ed8582e958dfa07533b"));
        }

        [Test]
        public void Keccak256_IsKeccakNotSha3()
        {
            // Keccak-256 and NIST SHA3-256 differ only in padding, so an implementation
            // that reaches for SHA3 produces a plausible looking but invalid digest.
            var hash = Convert.ToHexString(CasperEip712.Keccak256([])).ToLowerInvariant();

            Assert.That(hash, Is.EqualTo("c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470"));
        }

        [Test]
        public void EncodeAddress_HashesCasper33ByteAddress()
        {
            var address = Convert.FromHexString("00" + ReferenceHash);

            var slot = CasperEip712.EncodeAddress(address);

            // A Casper address does not fit a 32 byte slot, so it is hashed rather than padded.
            Assert.That(slot, Is.EqualTo(CasperEip712.Keccak256(address)));
        }

        [Test]
        public void EncodeAddress_LeftPadsEthereum20ByteAddress()
        {
            var address = Convert.FromHexString("7d95514aed9f13aa89c8e5ed9c29d08e8e9bfa37");

            var slot = CasperEip712.EncodeAddress(address);

            Assert.That(slot, Has.Length.EqualTo(32));
            Assert.That(slot.Take(12), Is.All.Zero);
            Assert.That(slot.Skip(12), Is.EqualTo(address));
        }

        [Test]
        public void EncodeAddress_RejectsOtherLengths()
        {
            Assert.Throws<ArgumentException>(() => CasperEip712.EncodeAddress(new byte[32]));
        }

        [Test]
        public void EncodeUInt256_IsBigEndianAndZeroPadded()
        {
            var slot = CasperEip712.EncodeUInt256(new BigInteger(1000000));

            Assert.That(Convert.ToHexString(slot).ToLowerInvariant(),
                Is.EqualTo("00000000000000000000000000000000000000000000000000000000000f4240"));
        }

        [Test]
        public void EncodeUInt256_AcceptsFullWidthValues()
        {
            var max = BigInteger.Pow(2, 256) - 1;

            var slot = CasperEip712.EncodeUInt256(max);

            Assert.That(slot, Has.Length.EqualTo(32));
            Assert.That(slot, Is.All.EqualTo((byte)255));
        }

        [Test]
        public void EncodeUInt256_RejectsNegativeValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CasperEip712.EncodeUInt256(BigInteger.MinusOne));
        }

        [Test]
        public void EncodeUInt256_RejectsOverflow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CasperEip712.EncodeUInt256(BigInteger.Pow(2, 256)));
        }

        [Test]
        public void HashDomain_RejectsWrongSizedPackageHash()
        {
            Assert.Throws<ArgumentException>(() =>
                CasperEip712.HashDomain("Wrapped CSPR", "1", CasperNetworks.Testnet, new byte[31]));
        }

        [Test]
        public void HashDomain_DependsOnEveryField()
        {
            var hash = Convert.FromHexString(ReferenceHash);
            var baseline = CasperEip712.HashDomain("Wrapped CSPR", "1", CasperNetworks.Testnet, hash);

            Assert.Multiple(() =>
            {
                Assert.That(CasperEip712.HashDomain("Wrapped CSPR2", "1", CasperNetworks.Testnet, hash), Is.Not.EqualTo(baseline));
                Assert.That(CasperEip712.HashDomain("Wrapped CSPR", "2", CasperNetworks.Testnet, hash), Is.Not.EqualTo(baseline));
                Assert.That(CasperEip712.HashDomain("Wrapped CSPR", "1", CasperNetworks.Mainnet, hash), Is.Not.EqualTo(baseline));
                Assert.That(CasperEip712.HashDomain("Wrapped CSPR", "1", CasperNetworks.Testnet, new byte[32]), Is.Not.EqualTo(baseline));
            });
        }

        [Test]
        public void HashTransferWithAuthorization_RejectsWrongSizedNonce()
        {
            var address = Convert.FromHexString("00" + ReferenceHash);

            Assert.Throws<ArgumentException>(() =>
                CasperEip712.HashTransferWithAuthorization(address, address, BigInteger.One, 1, 2, new byte[16]));
        }

        [Test]
        public void HashTypedData_RejectsWrongSizedInputs()
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentException>(() => CasperEip712.HashTypedData(new byte[31], new byte[32]));
                Assert.Throws<ArgumentException>(() => CasperEip712.HashTypedData(new byte[32], new byte[31]));
            });
        }

        [Test]
        public void TypeStrings_AreTheCanonicalCasperOnes()
        {
            // The type strings are hashed into the digest, so their exact text,
            // including field order, is part of the wire protocol.
            Assert.Multiple(() =>
            {
                Assert.That(CasperEip712.DomainTypeString,
                    Is.EqualTo("EIP712Domain(string name,string version,string chain_name,bytes32 contract_package_hash)"));
                Assert.That(CasperEip712.TransferWithAuthorizationTypeString,
                    Is.EqualTo("TransferWithAuthorization(address from,address to,uint256 value,uint256 validAfter,uint256 validBefore,bytes32 nonce)"));
            });
        }
    }
}
