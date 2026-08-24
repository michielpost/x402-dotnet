using System.Numerics;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;

namespace x402.Client.Casper
{
    /// <summary>
    /// Casper-native EIP-712 typed data hashing, as implemented by the Casper x402
    /// mechanism and enforced by the Casper facilitator.
    /// </summary>
    /// <remarks>
    /// Casper reuses the EIP-712 construction but with a chain-specific domain:
    /// <c>EIP712Domain(string name,string version,string chain_name,bytes32 contract_package_hash)</c>.
    /// There is no <c>chainId</c> and no <c>verifyingContract</c>; the CEP-18 contract
    /// package hash takes the place of the verifying contract and the CAIP-2 network id
    /// takes the place of the chain id.
    /// <para>
    /// Two details differ from Ethereum and are easy to get wrong:
    /// a Casper address is 33 bytes, so it is encoded as <c>keccak256(address)</c>
    /// rather than left-padded into a 32 byte slot, and the hash function is the
    /// original Keccak-256, not the NIST SHA3-256 variant.
    /// </para>
    /// </remarks>
    public static class CasperEip712
    {
        /// <summary>
        /// The Casper domain type string. The field order is part of the signed digest.
        /// </summary>
        public const string DomainTypeString =
            "EIP712Domain(string name,string version,string chain_name,bytes32 contract_package_hash)";

        /// <summary>
        /// The type string of the CEP-18 transfer authorization that x402 signs.
        /// </summary>
        public const string TransferWithAuthorizationTypeString =
            "TransferWithAuthorization(address from,address to,uint256 value,uint256 validAfter,uint256 validBefore,bytes32 nonce)";

        /// <summary>
        /// Computes the Keccak-256 hash of <paramref name="data"/>.
        /// </summary>
        /// <param name="data">The bytes to hash.</param>
        /// <returns>The 32 byte digest.</returns>
        public static byte[] Keccak256(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);

            var digest = new KeccakDigest(256);
            digest.BlockUpdate(data, 0, data.Length);

            var output = new byte[32];
            digest.DoFinal(output, 0);
            return output;
        }

        /// <summary>
        /// Computes the domain separator for a CEP-18 token on a Casper network.
        /// </summary>
        /// <param name="name">Token name, from <c>extra.name</c> of the payment requirements.</param>
        /// <param name="version">Token version, from <c>extra.version</c> of the payment requirements.</param>
        /// <param name="chainName">CAIP-2 network id, for example <c>casper:casper-test</c>.</param>
        /// <param name="contractPackageHash">The 32 byte CEP-18 contract package hash.</param>
        /// <returns>The 32 byte domain separator.</returns>
        public static byte[] HashDomain(string name, string version, string chainName, byte[] contractPackageHash)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(version);
            ArgumentNullException.ThrowIfNull(chainName);
            ArgumentNullException.ThrowIfNull(contractPackageHash);

            if (contractPackageHash.Length != 32)
                throw new ArgumentException($"Contract package hash must be 32 bytes, got {contractPackageHash.Length}.", nameof(contractPackageHash));

            var buffer = new List<byte>(32 * 5);
            buffer.AddRange(Keccak256(Encoding.UTF8.GetBytes(DomainTypeString)));
            buffer.AddRange(EncodeString(name));
            buffer.AddRange(EncodeString(version));
            buffer.AddRange(EncodeString(chainName));
            buffer.AddRange(contractPackageHash);

            return Keccak256(buffer.ToArray());
        }

        /// <summary>
        /// Computes the struct hash of a CEP-18 transfer authorization.
        /// </summary>
        /// <param name="from">Payer account hash address (33 bytes: a <c>00</c> tag followed by the account hash).</param>
        /// <param name="to">Recipient account hash address (33 bytes).</param>
        /// <param name="value">Payment amount in the token's smallest unit.</param>
        /// <param name="validAfter">Unix timestamp in seconds after which the authorization is valid.</param>
        /// <param name="validBefore">Unix timestamp in seconds after which the authorization has expired.</param>
        /// <param name="nonce">The 32 byte replay protection nonce.</param>
        /// <returns>The 32 byte struct hash.</returns>
        public static byte[] HashTransferWithAuthorization(
            byte[] from,
            byte[] to,
            BigInteger value,
            long validAfter,
            long validBefore,
            byte[] nonce)
        {
            ArgumentNullException.ThrowIfNull(from);
            ArgumentNullException.ThrowIfNull(to);
            ArgumentNullException.ThrowIfNull(nonce);

            if (nonce.Length != 32)
                throw new ArgumentException($"Nonce must be 32 bytes, got {nonce.Length}.", nameof(nonce));

            var buffer = new List<byte>(32 * 7);
            buffer.AddRange(Keccak256(Encoding.UTF8.GetBytes(TransferWithAuthorizationTypeString)));
            buffer.AddRange(EncodeAddress(from));
            buffer.AddRange(EncodeAddress(to));
            buffer.AddRange(EncodeUInt256(value));
            buffer.AddRange(EncodeUInt256(validAfter));
            buffer.AddRange(EncodeUInt256(validBefore));
            buffer.AddRange(nonce);

            return Keccak256(buffer.ToArray());
        }

        /// <summary>
        /// Combines a domain separator and a struct hash into the final digest that is signed:
        /// <c>keccak256(0x19 || 0x01 || domainSeparator || structHash)</c>.
        /// </summary>
        /// <param name="domainSeparator">The 32 byte domain separator.</param>
        /// <param name="structHash">The 32 byte struct hash.</param>
        /// <returns>The 32 byte digest to sign.</returns>
        public static byte[] HashTypedData(byte[] domainSeparator, byte[] structHash)
        {
            ArgumentNullException.ThrowIfNull(domainSeparator);
            ArgumentNullException.ThrowIfNull(structHash);

            if (domainSeparator.Length != 32)
                throw new ArgumentException($"Domain separator must be 32 bytes, got {domainSeparator.Length}.", nameof(domainSeparator));
            if (structHash.Length != 32)
                throw new ArgumentException($"Struct hash must be 32 bytes, got {structHash.Length}.", nameof(structHash));

            var buffer = new byte[2 + 32 + 32];
            buffer[0] = 0x19;
            buffer[1] = 0x01;
            Array.Copy(domainSeparator, 0, buffer, 2, 32);
            Array.Copy(structHash, 0, buffer, 34, 32);

            return Keccak256(buffer);
        }

        /// <summary>
        /// Encodes a dynamic string as an EIP-712 slot.
        /// </summary>
        /// <param name="value">The string to encode.</param>
        /// <returns>The 32 byte slot.</returns>
        public static byte[] EncodeString(string value) => Keccak256(Encoding.UTF8.GetBytes(value));

        /// <summary>
        /// Encodes an address as an EIP-712 slot. A 33 byte Casper address hashes to its
        /// slot; a 20 byte address is left-padded, matching the Ethereum rule.
        /// </summary>
        /// <param name="address">The raw address bytes.</param>
        /// <returns>The 32 byte slot.</returns>
        public static byte[] EncodeAddress(byte[] address)
        {
            if (address.Length == 33)
                return Keccak256(address);

            if (address.Length == 20)
            {
                var padded = new byte[32];
                Array.Copy(address, 0, padded, 12, 20);
                return padded;
            }

            throw new ArgumentException($"Invalid address length {address.Length}; expected 33 bytes for Casper.", nameof(address));
        }

        /// <summary>
        /// Encodes a non-negative integer as a big-endian EIP-712 uint256 slot.
        /// </summary>
        /// <param name="value">The value to encode.</param>
        /// <returns>The 32 byte slot.</returns>
        public static byte[] EncodeUInt256(BigInteger value)
        {
            if (value.Sign < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "uint256 value must not be negative.");

            var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
            if (bytes.Length > 32)
                throw new ArgumentOutOfRangeException(nameof(value), "uint256 value exceeds 32 bytes.");

            var slot = new byte[32];
            Array.Copy(bytes, 0, slot, 32 - bytes.Length, bytes.Length);
            return slot;
        }
    }
}
