using Autofac;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// Dependency injection module
/// </summary>
public class Module : Autofac.Module
{
    /// <summary>
    /// Add the services for storage tests
    /// </summary>
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterModule(new Storage.Module(_ => StorageConfiguration.InMemory()));
        // #270: Storage.Module no longer references/registers Application; this composition root now
        // registers Application.Module and Infrastructure's secret seams explicitly (previously these
        // were pulled in transitively via Storage.Module).
        builder.RegisterModule<Proxytrace.Application.Module>();
        builder.RegisterModule<Proxytrace.Infrastructure.Security.SecretProtectionModule>();
        builder.RegisterStub<IModelClient>();
        builder.RegisterStub<IProviderClient>();
    }
}