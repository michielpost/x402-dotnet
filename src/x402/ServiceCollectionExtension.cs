using Microsoft.Extensions.DependencyInjection;
using x402.Channels;
using x402.Core;
using x402.Core.Interfaces;
using x402.Facilitator;

namespace x402
{
    public static class ServiceCollectionExtension
    {
        public static IServiceCollection AddX402(this IServiceCollection services)
        {
            services.AddSingleton<X402HandlerV2>();
            services.AddSingleton<IAssetInfoProvider, AssetInfoProvider>();
            services.AddHttpContextAccessor();

            return services;
        }

        /// <summary>
        /// Registers a <see cref="ChannelManager"/> for the "batch-settlement" scheme.
        /// Requests using batch-settlement are recorded as off-chain vouchers and batched
        /// into periodic on-chain settlements. Call <see cref="ChannelManager.Start"/> to
        /// begin the background claim, settle and refund cycles.
        /// </summary>
        public static IServiceCollection AddX402ChannelManager(this IServiceCollection services)
        {
            services.AddSingleton<ChannelManager>();

            return services;
        }

        public static IServiceCollection WithHttpFacilitator(this IServiceCollection services, string facilitatorUrl)
        {
            services.AddHttpClient<IFacilitatorV2Client, HttpFacilitatorClient>(client =>
            {
                client.BaseAddress = new Uri(facilitatorUrl);
            });

            return services;
        }
    }
}
