using Autofac;
using Autofac.Core;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nordstein.Core.Common.DependencyInjection;
using Proxytrace.Infrastructure.Security;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.ModelProvider;

namespace Proxytrace.Proxy.Tests;

/// <summary>
/// Guards the proxy host's secret-seam wiring. The lean proxy loads storage with
/// <c>registerApplicationServices: false</c>, so it must register <see cref="SecretProtectionModule"/>
/// itself: the storage repositories it resolves during API-key resolution map secret-bearing columns
/// and cannot be constructed without <c>ISecretHasher</c> / <c>ISecretProtector</c>. Forgetting the
/// seams makes every proxied request fail with an opaque 500 (an unhandled DI resolution failure) —
/// the regression this test pins. See docs/security.md.
/// </summary>
[TestClass]
public sealed class SecretSeamWiringTests
{
    // Reproduces the proxy host's container shape: storage repositories without the application layer,
    // plus the stubs the storage model graph needs but the proxy never exercises (mirrors Proxy.Module).
    private static IContainer BuildProxyStorageScope(bool registerSecretSeams)
    {
        var builder = new ContainerBuilder();

        builder.RegisterModule(new Proxytrace.Storage.Module(
            _ => Proxytrace.Storage.StorageConfiguration.InMemory(),
            registerApplicationServices: false));

        builder.RegisterInstance(Substitute.For<IAgentNameGenerator>()).As<IAgentNameGenerator>();
        builder.RegisterInstance(Substitute.For<IProviderClient>()).As<IProviderClient>();
        builder.RegisterServiceCollection(services => services.AddLogging());

        if (registerSecretSeams)
        {
            builder.RegisterModule<SecretProtectionModule>();
        }

        return builder.Build();
    }

    [TestMethod]
    public void ProxyStorageScope_WithSecretSeams_ResolvesSecretBearingRepositories()
    {
        using var container = BuildProxyStorageScope(registerSecretSeams: true);

        container.Resolve<IModelProviderRepository>().Should().NotBeNull();
        container.Resolve<IApiKeyRepository>().Should().NotBeNull();
    }

    [TestMethod]
    public void ProxyStorageScope_WithoutSecretSeams_CannotResolveModelProviderRepository()
    {
        using var container = BuildProxyStorageScope(registerSecretSeams: false);

        var resolve = () => container.Resolve<IModelProviderRepository>();

        resolve.Should().Throw<DependencyResolutionException>();
    }
}
