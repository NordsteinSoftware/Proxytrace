using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nordstein.Core.Common.DependencyInjection;
using Proxytrace.Licensing.Internal;

namespace Proxytrace.Licensing;

/// <summary>
/// Autofac module wiring the licensing subsystem. The configuration (including the resolved
/// license JWT) is supplied by the composition root.
/// </summary>
public sealed class Module : Autofac.Module
{
    /// <summary>
    /// Autofac <c>builder.Properties</c> key marking that a licensing module has already been
    /// registered by a composition root with a real configuration. Downstream modules check it to
    /// skip their Free-tier fallback registration (and to avoid registering twice).
    /// </summary>
    public const string RegisteredKey = "Proxytrace.Licensing.Registered";

    private readonly LicensingConfiguration configuration;

    public Module(LicensingConfiguration configuration)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder.RegisterInstance(configuration).SingleInstance();

        builder.RegisterType<JwtLicenseValidator>()
            .As<IJwtLicenseValidator>()
            .SingleInstance();

        builder.RegisterType<LicenseCacheStore>()
            .As<ILicenseCacheStore>()
            .SingleInstance();

        builder.RegisterType<ConfiguredLicenseResolver>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<LicenseActivator>()
            .As<ILicenseActivator>()
            .SingleInstance();

        // AutoActivate forces the constructor (and thus the synchronous startup resolution) to
        // run at container build time, so the resolved tier is logged and in force before any
        // request is served. An invalid configured JWT no longer crashes the host — it degrades
        // to Free entitlements with LicenseStatus.Invalid and is surfaced in the UI.
        builder.RegisterType<LicenseService>()
            .As<ILicenseService>()
            .AsSelf()
            .SingleInstance()
            .AutoActivate();

        builder.RegisterType<LicenseCheckService>()
            .As<ILicenseRefreshTrigger>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterServiceCollection(services =>
        {
            services.AddHttpClient<ILicenseServerClient, LicenseServerClient>("license-server", client =>
            {
                client.BaseAddress = new Uri(configuration.ServerUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            services.AddHostedService(sp => sp.GetRequiredService<LicenseCheckService>());
        });
    }
}
