using Nordstein.Core.AI.Clients;
using Autofac;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Storage;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

public class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);
        builder.RegisterModule<Domain.Module>();
        builder.RegisterModule(new Storage.Module(_ => StorageConfiguration.InMemory()));
        // #270: Storage.Module no longer references/registers Application; this composition root now
        // registers Application.Module and Infrastructure's secret seams explicitly (previously these
        // were pulled in transitively via Storage.Module).
        builder.RegisterModule<Proxytrace.Application.Module>();
        builder.RegisterModule<Proxytrace.Infrastructure.Security.SecretProtectionModule>();
        builder.RegisterStub<IModelClient>();
        builder.RegisterStub<IProviderClient>();
        builder.RegisterInstance(Prompts.ResourceManager);
    }
}
