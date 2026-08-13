using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nordstein.Core.Common.DependencyInjection;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Common.Text;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Proxytrace.Infrastructure.Security;
using Proxytrace.Proxy.Internal;

namespace Proxytrace.Proxy.Tests;

/// <summary>
/// Production-shape freshness guarantees for the proxy's credential resolution (#407): rotation and
/// revocation must take effect on the very next request, with no TTL window. These tests compose the
/// proxy host's real container shape — <c>Storage.Module</c> without application services, the
/// at-rest secret seams (so provider keys round-trip through real encryption and the blind-index
/// hash), and the resolver registered per lifetime scope exactly as <c>Proxy.Module</c> does — then
/// resolve each request from a fresh scope, mirroring the per-request scoping of the host. The
/// rotation itself runs the same delegate + repository update the API's
/// <c>ModelProvidersController.Update</c> runs in its separate process.
/// </summary>
[TestClass]
public sealed class ApiKeyResolverRotationTests
{
    [TestMethod]
    public async Task ResolveAsync_AfterUpstreamKeyRotation_ForwardsTheNewKeyImmediately()
    {
        await using var container = BuildProxyShapedContainer();
        var (rawKey, provider) = await SeedProxytraceKeyAsync(container);

        // Warm resolution (in production this is any earlier proxied request).
        var before = await ResolveOnceAsync(container, rawKey, projectSlug: null);
        before.Should().NotBeNull();
        before.Provider.ApiKey.Should().Be(provider.ApiKey);

        await RotateUpstreamKeyAsync(container, provider.Id, "upstream-key-B");

        // The very next request must forward the rotated key — no TTL window.
        var after = await ResolveOnceAsync(container, rawKey, projectSlug: null);
        after.Should().NotBeNull();
        after.Provider.ApiKey.Should().Be("upstream-key-B");
    }

    [TestMethod]
    public async Task ResolveAsync_AfterUpstreamKeyRotation_TheOldKeyStopsAuthenticatingImmediately()
    {
        await using var container = BuildProxyShapedContainer();
        var (project, provider) = await SeedProviderWithUpstreamKeyAsync(container, "upstream-key-A");
        string slug = project.Name.ToSlug();

        // The provider's own key authenticates at the proxy (upstream-key path) — warm it.
        (await ResolveOnceAsync(container, "upstream-key-A", slug)).Should().NotBeNull();

        await RotateUpstreamKeyAsync(container, provider.Id, "upstream-key-B");

        // Revocation semantics: the replaced credential must be rejected on the next request,
        // and the replacement must authenticate.
        (await ResolveOnceAsync(container, "upstream-key-A", slug)).Should().BeNull();
        (await ResolveOnceAsync(container, "upstream-key-B", slug)).Should().NotBeNull();
    }

    [TestMethod]
    public async Task ResolveAsync_AfterProxytraceKeyRemoval_RejectsTheKeyImmediately()
    {
        await using var container = BuildProxyShapedContainer();
        var (rawKey, _) = await SeedProxytraceKeyAsync(container);

        (await ResolveOnceAsync(container, rawKey, projectSlug: null)).Should().NotBeNull();

        await using (var scope = container.BeginLifetimeScope())
        {
            var keys = scope.Resolve<IRepository<IApiKey>>();
            var key = await keys.FindFirstAsync(CancellationToken.None);
            key.Should().NotBeNull();
            (await keys.RemoveAsync(key.Id, CancellationToken.None)).Should().BeTrue();
        }

        (await ResolveOnceAsync(container, rawKey, projectSlug: null)).Should().BeNull();
    }

    // Mirrors Proxy.Module: storage without application services, the secret seams, the stubs the
    // storage model graph needs, and the per-lifetime-scope resolver registration.
    private static IContainer BuildProxyShapedContainer()
    {
        var builder = new ContainerBuilder();

        builder.RegisterModule(new Proxytrace.Storage.Module(
            _ => Proxytrace.Storage.StorageConfiguration.InMemory(),
            registerApplicationServices: false));
        builder.RegisterModule<SecretProtectionModule>();

        builder.RegisterInstance(Substitute.For<IAgentNameGenerator>()).As<IAgentNameGenerator>();
        builder.RegisterInstance(Substitute.For<IProviderClient>()).As<IProviderClient>();
        builder.RegisterServiceCollection(services => services.AddLogging());

        builder.RegisterType<ApiKeyResolver>()
            .As<IApiKeyResolver>()
            .InstancePerLifetimeScope();

        return builder.Build();
    }

    // One resolution from a fresh lifetime scope — the per-request shape of the proxy host.
    private static async Task<ResolvedApiKey?> ResolveOnceAsync(IContainer container, string rawKey, string? projectSlug)
    {
        await using var scope = container.BeginLifetimeScope();
        var resolver = scope.Resolve<IApiKeyResolver>();
        return await resolver.ResolveAsync(rawKey, projectSlug, CancellationToken.None);
    }

    // Seeds a Proxytrace-issued ingestion key with a known raw value, hashed at the creation site
    // exactly as ModelProvidersController.CreateKey does.
    private static async Task<(string RawKey, IModelProvider Provider)> SeedProxytraceKeyAsync(IContainer container)
    {
        await using var scope = container.BeginLifetimeScope();
        var project = await scope.Resolve<IDomainEntityGenerator<IProject>>().GetOrCreateAsync(CancellationToken.None);
        var provider = await scope.Resolve<IDomainEntityGenerator<IModelProvider>>().GetOrCreateAsync(CancellationToken.None);
        var owner = await scope.Resolve<IDomainEntityGenerator<IUser>>().GetOrCreateAsync(CancellationToken.None);

        var raw = "proxytrace-rotation-test-key";
        var key = scope.Resolve<IApiKey.CreateNew>()(
            name: "rotation test key",
            keyHash: Sha256.HexHash(raw),
            keyPrefix: raw[..16],
            project: project,
            provider: provider,
            scopes: ApiKeyScopes.Ingestion,
            owner: owner);
        await scope.Resolve<IRepository<IApiKey>>().AddAsync(key, CancellationToken.None);
        return (raw, provider);
    }

    private static async Task<(IProject Project, IModelProvider Provider)> SeedProviderWithUpstreamKeyAsync(
        IContainer container,
        string upstreamKey)
    {
        await using var scope = container.BeginLifetimeScope();
        var project = await scope.Resolve<IDomainEntityGenerator<IProject>>().GetOrCreateAsync(CancellationToken.None);
        var provider = scope.Resolve<IModelProvider.CreateNew>()(
            name: "Rotation Provider",
            endpoint: new Uri("https://api.rotation.example.com/v1"),
            apiKey: upstreamKey,
            kind: ModelProviderKind.OpenAiCompatible);
        provider = await scope.Resolve<IModelProviderRepository>().AddAsync(provider, CancellationToken.None);
        return (project, provider);
    }

    // The same reconstitute-and-update the API's ModelProvidersController.Update performs — in a
    // separate lifetime scope, standing in for the separate API process.
    private static async Task RotateUpstreamKeyAsync(IContainer container, Guid providerId, string newKey)
    {
        await using var scope = container.BeginLifetimeScope();
        var repository = scope.Resolve<IModelProviderRepository>();
        var existing = await repository.FindAsync(providerId, CancellationToken.None);
        existing.Should().NotBeNull();
        var rotated = scope.Resolve<IModelProvider.CreateExisting>()(
            existing.Name,
            existing.Endpoint,
            newKey,
            existing.Kind,
            existing);
        await repository.UpdateAsync(rotated, CancellationToken.None);
    }
}
