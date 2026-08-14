using Autofac;

namespace Proxytrace.Licensing.Tests;

/// <summary>
/// DI module for licensing tests. Registers Common + the Proxytrace licensing module (which
/// wires the Nordstein.Core engine with the product policy) using a test-generated keypair and
/// no real license JWT (Free tier). Individual tests override the configuration via
/// GetServices(action). Nothing here reaches the network or filesystem: the license-server
/// client and cache store are only resolved by the background check service, which tests never
/// start.
/// </summary>
public sealed class Module : Autofac.Module
{
    internal static readonly TestLicenseFactory Factory = new();

    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder.RegisterModule<Nordstein.Core.Common.Module>();
        builder.RegisterModule(new Proxytrace.Licensing.Module(Factory.Configuration()));
    }
}
