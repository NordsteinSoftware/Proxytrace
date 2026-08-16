using Autofac;
using Proxytrace.Licensing.Internal;
using Core = Nordstein.Core.Licensing;

namespace Proxytrace.Licensing;

/// <summary>
/// Autofac module wiring the licensing subsystem: the product-agnostic engine from
/// Nordstein.Core.Licensing, parameterized with Proxytrace's identity (issuer, audience, trust
/// root) and tier policy, plus the enum-typed adapters the rest of the application consumes.
/// The configuration (including the resolved license JWT) is supplied by the composition root.
/// </summary>
public sealed class Module : Autofac.Module
{
    /// <summary>
    /// Autofac <c>builder.Properties</c> key marking that a licensing module has already been
    /// registered by a composition root with a real configuration. Downstream modules check it to
    /// skip their Free-tier fallback registration (and to avoid registering twice).
    /// </summary>
    public const string RegisteredKey = "Proxytrace.Licensing.Registered";

    // The identity the license server signs Proxytrace licenses under. Together with the trust
    // root in LicensePublicKeys this is what makes a license a *Proxytrace* license; the
    // verification machinery itself lives in Nordstein.Core.Licensing.
    private const string Issuer = "https://license.proxytrace.dev";
    private const string Audience = "proxytrace";

    private readonly LicensingConfiguration configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="Module"/> class.
    /// </summary>
    public Module(LicensingConfiguration configuration)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder.RegisterModule(new Core.LicensingModule(
            ToCoreConfiguration(configuration),
            new ProxytraceLicenseTierPolicy()));

        builder.RegisterInstance(configuration).SingleInstance();

        // The engine resolves its configuration from the container, so derive it from the
        // (possibly test-overridden) product configuration rather than only the constructor
        // argument. This later registration wins over the instance the engine module registered.
        builder.Register(c => ToCoreConfiguration(c.Resolve<LicensingConfiguration>()))
            .SingleInstance();

        builder.RegisterType<LicenseServiceAdapter>()
            .As<ILicenseService>()
            .SingleInstance();

        builder.RegisterType<LicenseActivatorAdapter>()
            .As<ILicenseActivator>()
            .SingleInstance();
    }

    private static Core.LicensingConfiguration ToCoreConfiguration(LicensingConfiguration configuration) => new()
    {
        Issuer = Issuer,
        Audience = Audience,
        ServerUrl = configuration.ServerUrl,
        PublicKeys = configuration.PublicKeys,
        LicenseJwt = configuration.LicenseJwt,
        OverrideSnapshot = configuration.OverrideSnapshot is { } snapshot
            ? LicenseSnapshotMapper.ToCore(snapshot)
            : null,
        ServerCheckEnabled = configuration.ServerCheckEnabled,
        CheckIntervalHours = configuration.CheckIntervalHours,
        OfflineGracePeriodDays = configuration.OfflineGracePeriodDays,
        CacheFilePath = configuration.CacheFilePath,
    };
}
