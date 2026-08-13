using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Nordstein.Core.Common.DependencyInjection;
using Nordstein.Core.Common.Lifecycle;

namespace Nordstein.Core.Testing;

internal class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);
        builder
            .Register(sp => new AutofacServiceProvider(sp.Resolve<ILifetimeScope>()))
            .As<IServiceProvider>();
        
        builder.Register(c =>
        {
            var dir = c.Resolve<ITempDirectory.Create>()();
            var env = Substitute.For<IHostEnvironment>();
            env.ContentRootPath.Returns(dir.Path);
            return env;
        }).SingleInstance();

        builder.RegisterServiceCollection(sc => sc.AddLogging());

        // Entity mappers and stores that protect secrets at rest (ModelProviderConfig,
        // EmailSettingsStore) depend on ISecretProtector -> IDataProtectionProvider, which the
        // mapper resolves eagerly whenever a StorageDbContext model is built. Register an ephemeral
        // (non-persisted) Data Protection key ring so every test container can construct it.
        builder.RegisterServiceCollection(sc => sc.AddDataProtection());
    }
}