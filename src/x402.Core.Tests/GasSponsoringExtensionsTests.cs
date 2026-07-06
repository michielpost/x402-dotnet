using x402.Core.Extensions;
using x402.Core.Models.Facilitator;
using x402.Core.Models.v2;

namespace x402.Core.Tests
{
    [TestFixture]
    public class GasSponsoringExtensionsTests
    {
        [Test]
        public void DeclareEip2612GasSponsoringExtension_UsesWellKnownKey()
        {
            var extension = GasSponsoringExtensions.DeclareEip2612GasSponsoringExtension();

            Assert.That(extension.Key, Is.EqualTo("eip2612-gas-sponsoring"));
            Assert.That(extension.Value, Is.Not.Null);
        }

        [Test]
        public void DeclareErc20ApprovalGasSponsoringExtension_UsesWellKnownKey()
        {
            var extension = GasSponsoringExtensions.DeclareErc20ApprovalGasSponsoringExtension();

            Assert.That(extension.Key, Is.EqualTo("erc20-approval-gas-sponsoring"));
            Assert.That(extension.Value, Is.Not.Null);
        }

        [Test]
        public void With_AddsExtensionToNullDictionary()
        {
            Dictionary<string, ExtensionData>? extensions = null;

            extensions = extensions.With(GasSponsoringExtensions.DeclareEip2612GasSponsoringExtension());

            Assert.That(extensions, Contains.Key(X402ExtensionKeys.Eip2612GasSponsoring));
        }

        [Test]
        public void With_ChainsMultipleExtensions()
        {
            var extensions = new Dictionary<string, ExtensionData>()
                .With(GasSponsoringExtensions.DeclareEip2612GasSponsoringExtension())
                .With(GasSponsoringExtensions.DeclareErc20ApprovalGasSponsoringExtension());

            Assert.That(extensions, Has.Count.EqualTo(2));
        }

        [Test]
        public void SupportsExtension_TrueWhenFacilitatorListsIt()
        {
            var supported = new SupportedResponse
            {
                Extensions = new List<string> { X402ExtensionKeys.Eip2612GasSponsoring }
            };

            Assert.That(supported.SupportsExtension(X402ExtensionKeys.Eip2612GasSponsoring), Is.True);
            Assert.That(supported.SupportsExtension(X402ExtensionKeys.Erc20ApprovalGasSponsoring), Is.False);
        }

        [Test]
        public void SupportsExtension_FalseWhenExtensionsMissing()
        {
            var supported = new SupportedResponse();

            Assert.That(supported.SupportsExtension(X402ExtensionKeys.Eip2612GasSponsoring), Is.False);
        }
    }
}
