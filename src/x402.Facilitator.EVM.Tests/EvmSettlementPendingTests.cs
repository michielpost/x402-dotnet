using System.Numerics;
using Moq;
using Nethereum.ABI.EIP712;
using Nethereum.Contracts;
using Nethereum.Contracts.Services;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.RPC.Eth.Services;
using Nethereum.RPC.Eth.Transactions;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;
using Nethereum.Web3;
using x402.Core.Enums;
using x402.Core.Models.Facilitator;
using x402.Core.Models.v2;

namespace x402.Facilitator.EVM.Tests
{
    /// <summary>
    /// Settlement behaviour of EvmPaymentService when the receiveWithAuthorization transfer is
    /// broadcast but the receipt is not (yet) available, or when the authorization's nonce has
    /// already been used. These cases cover the x402 v2 §9 pending state: an already-broadcast hash
    /// must be carried, not discarded; a reused nonce must not be re-broadcast; and the result must
    /// not read as a terminal failure that makes a pessimistic seller ask the payer to sign again.
    /// </summary>
    [TestFixture]
    public class EvmSettlementPendingTests
    {
        // Hardhat well-known test account #0 (public test vector, no value at risk).
        private const string PrivateKey = "ac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80";
        private const string PayTo = "0x209693Bc6afc0C5328bA36FaF03C514EF312287C";
        private const string Asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e";
        private const string Network = "eip155:84532"; // base-sepolia, chainId 84532
        private const string FacilitatorAddress = "0x1111111111111111111111111111111111111111";
        private const string TxHash = "0xabc1230000000000000000000000000000000000000000000000000000000001";

        // A minimal EIP-3009 ABI with the functions settlement calls. The returned contract only
        // encodes calldata in these tests (the authorizationState call is stubbed via the seam), so
        // no RPC service is needed.
        private const string Eip3009Abi = @"[
            {
                ""inputs"": [
                    { ""name"": ""from"", ""type"": ""address"" },
                    { ""name"": ""to"", ""type"": ""address"" },
                    { ""name"": ""value"", ""type"": ""uint256"" },
                    { ""name"": ""validAfter"", ""type"": ""uint256"" },
                    { ""name"": ""validBefore"", ""type"": ""uint256"" },
                    { ""name"": ""nonce"", ""type"": ""bytes32"" },
                    { ""name"": ""v"", ""type"": ""uint8"" },
                    { ""name"": ""r"", ""type"": ""bytes32"" },
                    { ""name"": ""s"", ""type"": ""bytes32"" }
                ],
                ""name"": ""receiveWithAuthorization"",
                ""outputs"": [],
                ""stateMutability"": ""nonpayable"",
                ""type"": ""function""
            }
        ]";

        /// <summary>
        /// Subclass that keeps the receipt poll fast and stubs the on-chain nonce check so the
        /// pending paths can be tested without a live node.
        /// </summary>
        private sealed class FastEvmPaymentService : EvmPaymentService
        {
            public FastEvmPaymentService(IWeb3 web3, string facilitatorAddress) : base(web3, facilitatorAddress) { }
            public bool AuthorizationUsed { get; set; }
            protected override int MaxReceiptAttempts => 2;
            protected override int ReceiptPollDelayMs => 0;
            protected override Task<bool> IsAuthorizationUsedAsync(Contract contract, string from, byte[] nonce)
                => Task.FromResult(AuthorizationUsed);
        }

        /// <summary>
        /// Builds a mocked IWeb3 whose receiveWithAuthorization broadcast returns <paramref name="txHash"/>
        /// and whose receipt lookups are driven by <paramref name="receipt"/>. Returns the send mock so a
        /// test can assert the transfer was (or was not) broadcast.
        /// </summary>
        private static (IWeb3 web3, Mock<IEthSendTransaction> send) BuildWeb3(
            string txHash, TransactionReceipt? receipt, bool receiptThrows = false, bool getContractThrows = false)
        {
            var send = new Mock<IEthSendTransaction>();
            send.Setup(s => s.SendRequestAsync(It.IsAny<TransactionInput>(), It.IsAny<object>()))
                .ReturnsAsync(txHash);

            var getReceipt = new Mock<IEthGetTransactionReceipt>();
            if (receiptThrows)
            {
                getReceipt.Setup(g => g.SendRequestAsync(It.IsAny<string>(), It.IsAny<object>()))
                    .ThrowsAsync(new IOException("rpc connection reset while polling for the receipt"));
            }
            else
            {
                getReceipt.Setup(g => g.SendRequestAsync(It.IsAny<string>(), It.IsAny<object>()))
                    .ReturnsAsync(receipt!);
            }

            var transactions = new Mock<IEthApiTransactionsService>();
            transactions.Setup(t => t.SendTransaction).Returns(send.Object);
            transactions.Setup(t => t.GetTransactionReceipt).Returns(getReceipt.Object);

            var eth = new Mock<IEthApiContractService>();
            if (getContractThrows)
            {
                eth.Setup(e => e.GetContract(It.IsAny<string>(), It.IsAny<string>()))
                    .Throws(new IOException("rpc error resolving the token contract"));
            }
            else
            {
                eth.Setup(e => e.GetContract(It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(new Contract(null!, Eip3009Abi, Asset));
            }
            eth.Setup(e => e.Transactions).Returns(transactions.Object);

            var web3 = new Mock<IWeb3>();
            web3.Setup(w => w.Eth).Returns(eth.Object);
            return (web3.Object, send);
        }

        /// <summary>
        /// Produces a payload with a genuine EIP-712 signature so VerifyPayment passes, mirroring the
        /// typed data the service recovers against.
        /// </summary>
        private static (PaymentPayloadHeader payload, PaymentRequirements requirements) BuildSignedPayload()
        {
            const string tokenName = "USDC";
            const string tokenVersion = "2";
            var chainId = new BigInteger(84532);

            var key = new EthECKey(PrivateKey);
            var from = key.GetPublicAddress();

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var validAfter = new BigInteger(now - 60);
            var validBefore = new BigInteger(now + 3600);
            var value = BigInteger.Parse("1000000");
            var nonceHex = "0x" + new string('1', 64); // 32 bytes
            var nonceBytes = Enumerable.Repeat((byte)0x11, 32).ToArray();

            var typedData = new TypedData<Domain>
            {
                Domain = new Domain
                {
                    Name = tokenName,
                    Version = tokenVersion,
                    ChainId = chainId,
                    VerifyingContract = Asset
                },
                Types = MemberDescriptionFactory.GetTypesMemberDescription(typeof(Domain), typeof(TransferWithAuthorization)),
                PrimaryType = nameof(TransferWithAuthorization),
            };

            var message = new TransferWithAuthorization
            {
                From = from,
                To = PayTo,
                Value = value,
                ValidAfter = validAfter,
                ValidBefore = validBefore,
                Nonce = nonceBytes
            };
            typedData.SetMessage(message);

            var signer = new Eip712TypedDataSigner();
            var signature = signer.SignTypedDataV4(message, typedData, key);

            var requirements = new PaymentRequirements
            {
                Scheme = PaymentScheme.Exact,
                Network = Network,
                Amount = "1000000",
                Asset = Asset,
                PayTo = PayTo,
                Extra = new PaymentRequirementsExtra { Name = tokenName, Version = tokenVersion }
            };

            var payload = new PaymentPayloadHeader
            {
                X402Version = 2,
                Accepted = new PaymentRequirements
                {
                    Scheme = PaymentScheme.Exact,
                    Network = Network,
                    Amount = "1000000",
                    Asset = Asset,
                    PayTo = PayTo,
                    Extra = new PaymentRequirementsExtra { Name = tokenName, Version = tokenVersion }
                },
                Payload = new Payload
                {
                    Signature = signature,
                    Authorization = new Authorization
                    {
                        From = from,
                        To = PayTo,
                        Value = value.ToString(),
                        ValidAfter = validAfter.ToString(),
                        ValidBefore = validBefore.ToString(),
                        Nonce = nonceHex
                    }
                }
            };

            return (payload, requirements);
        }

        private static TransactionReceipt MinedReceipt(int status) => new()
        {
            TransactionHash = TxHash,
            Status = new HexBigInteger(status)
        };

        [Test]
        public async Task Settle_WhenReceiptConfirms_ReturnsSuccessWithHash()
        {
            var (payload, requirements) = BuildSignedPayload();
            var (web3, _) = BuildWeb3(TxHash, MinedReceipt(1));
            var service = new FastEvmPaymentService(web3, FacilitatorAddress);

            var result = await service.SettlePayment(payload, requirements);

            Assert.That(result.Success, Is.True, "a confirmed receipt with status 1 must settle successfully");
            Assert.That(result.Transaction, Is.EqualTo(TxHash));
            Assert.That(result.ErrorReason, Is.Null);
        }

        [Test]
        public async Task Settle_WhenReceiptNeverArrives_ReturnsSettlementPendingWithHash()
        {
            var (payload, requirements) = BuildSignedPayload();
            var (web3, _) = BuildWeb3(TxHash, null);
            var service = new FastEvmPaymentService(web3, FacilitatorAddress);

            var result = await service.SettlePayment(payload, requirements);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorReason, Is.EqualTo(FacilitatorErrorCodes.SettlementPending),
                "a broadcast transfer with no receipt yet is pending, not a terminal invalid_transaction_state");
            Assert.That(result.Transaction, Is.EqualTo(TxHash),
                "the broadcast hash must be carried so the payer can reconcile on chain");
        }

        [Test]
        public async Task Settle_WhenReceiptPollThrowsAfterBroadcast_ReturnsSettlementPendingWithHash()
        {
            var (payload, requirements) = BuildSignedPayload();
            var (web3, _) = BuildWeb3(TxHash, null, receiptThrows: true);
            var service = new FastEvmPaymentService(web3, FacilitatorAddress);

            var result = await service.SettlePayment(payload, requirements);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorReason, Is.EqualTo(FacilitatorErrorCodes.SettlementPending),
                "an RPC error after the transfer was broadcast is pending, not unexpected_settle_error with no hash");
            Assert.That(result.Transaction, Is.EqualTo(TxHash),
                "the already-broadcast hash must not be lost when receipt polling fails");
        }

        [Test]
        public async Task Settle_WhenNonceAlreadyUsed_DoesNotReBroadcastAndReturnsPending()
        {
            var (payload, requirements) = BuildSignedPayload();
            var (web3, send) = BuildWeb3(TxHash, MinedReceipt(1));
            var service = new FastEvmPaymentService(web3, FacilitatorAddress) { AuthorizationUsed = true };

            var result = await service.SettlePayment(payload, requirements);

            send.Verify(s => s.SendRequestAsync(It.IsAny<TransactionInput>(), It.IsAny<object>()), Times.Never(),
                "a retry on an already-used nonce must not re-broadcast receiveWithAuthorization");
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorReason, Is.EqualTo(FacilitatorErrorCodes.SettlementPending),
                "a used nonce means a prior transfer consumed it: report pending so the payer reconciles, not a re-sign refusal");
        }

        [Test]
        public async Task Settle_WhenContractLookupThrowsBeforeBroadcast_ReturnsUnexpectedSettleError()
        {
            var (payload, requirements) = BuildSignedPayload();
            var (web3, send) = BuildWeb3(TxHash, null, getContractThrows: true);
            var service = new FastEvmPaymentService(web3, FacilitatorAddress);

            var result = await service.SettlePayment(payload, requirements);

            send.Verify(s => s.SendRequestAsync(It.IsAny<TransactionInput>(), It.IsAny<object>()), Times.Never(),
                "nothing is broadcast when the token contract cannot be resolved");
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorReason, Is.EqualTo(FacilitatorErrorCodes.UnexpectedSettleError),
                "an error before any broadcast is unexpected_settle_error, not pending");
            Assert.That(result.Transaction, Is.Empty,
                "no hash is reported when nothing was broadcast");
        }
    }
}
