using Nordstein.Core.AI.Clients;
using Autofac;
using Autofac.Core;
using Microsoft.Extensions.Configuration;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Infrastructure.Internal;

namespace Proxytrace.Infrastructure;

public class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);
        builder.RegisterModule<Proxytrace.Domain.Module>();

        builder.RegisterType<ModelClient>()
            .As<IModelClient>()
            .AsSelf();

        builder.RegisterType<ProviderClient>()
            .As<IProviderClient>()
            .WithParameter(ResolvedParameter.ForNamed<HttpClient>("pricing"));

        builder.Register(c =>
            {
                var cfg = c.Resolve<IConfiguration>().GetSection("Pricing");
                var defaults = new PricingOptions();
                return new PricingOptions
                {
                    LiteLlmFeedUrl = cfg["LiteLlmFeedUrl"] ?? defaults.LiteLlmFeedUrl,
                    FxApiUrl = cfg["FxApiUrl"] ?? defaults.FxApiUrl,
                };
            })
            .AsSelf()
            .SingleInstance();

        builder.Register(_ => new HttpClient())
            .Named<HttpClient>("pricing")
            .SingleInstance();

        builder.RegisterType<FrankfurterFxRateProvider>()
            .As<IFxRateProvider>()
            .WithParameter(ResolvedParameter.ForNamed<HttpClient>("pricing"))
            .SingleInstance();

        builder.RegisterType<LiteLlmCatalogResolver>()
            .AsSelf()
            .WithParameter(ResolvedParameter.ForNamed<HttpClient>("pricing"))
            .SingleInstance();

        builder.RegisterType<PricingService>()
            .As<IPricingService>()
            .SingleInstance();

        builder.Register(_ => new KioskEndpointOptions())
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(KioskEndpointOptions));
    }
}
