﻿using Microsoft.Extensions.DependencyInjection;
using x402.Core;
using x402.Core.Interfaces;
using x402.Facilitator;

namespace x402
{
    public static class ServiceCollectionExtension
    {
        public static IServiceCollection AddX402(this IServiceCollection services)
        {
            services.AddSingleton<X402Handler>();
            services.AddSingleton<ITokenInfoProvider, TokenInfoProvider>();
            services.AddHttpContextAccessor();

            return services;
        }

        public static IServiceCollection WithHttpFacilitator(this IServiceCollection services, string facilitatorUrl)
        {
            services.AddHttpClient<IFacilitatorClient, HttpFacilitatorClient>(client =>
            {
                client.BaseAddress = new Uri(facilitatorUrl);
            });

            return services;
        }
    }
}
