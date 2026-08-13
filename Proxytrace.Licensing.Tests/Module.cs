using Autofac;
using NSubstitute;
using Nordstein.Core.Common;
using Proxytrace.Licensing.Internal;

namespace Proxytrace.Licensing.Tests;

/// <summary>
/// DI module for licensing tests. Registers Common + Licensing with a test-generated
/// keypair and no real license JWT (Free tier). Individual tests override registrations
/// via GetServices(action) to supply stubs or specific configurations.
/// </summary>
public sealed class Module : Autofac.Module
{
    internal static readonly TestLicenseFactory Factory = new();

    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder.RegisterModule<Nordstein.Core.Common.Module>();
        builder.RegisterModule(new Proxytrace.Licensing.Module(Factory.Configuration()));

        // Replace the real LicenseCacheStore with a stub so tests don't touch the filesystem.
        builder.RegisterInstance(Substitute.For<ILicenseCacheStore>())
            .As<ILicenseCacheStore>()
            .SingleInstance();

        // Replace the real LicenseServerClient with a stub so tests don't hit the network.
        builder.RegisterInstance(Substitute.For<ILicenseServerClient>())
            .As<ILicenseServerClient>()
            .SingleInstance();
    }
}
