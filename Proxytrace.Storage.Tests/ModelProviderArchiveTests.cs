using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class ModelProviderArchiveTests : BaseTest<Module>
{
    [TestMethod]
    public async Task ArchiveAsync_ExcludesProviderFromPagedList()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IModelProviderRepository>();
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);

        await repository.ArchiveAsync(provider.Id, CancellationToken);

        var paged = await repository.GetPagedAsync(1, 100, CancellationToken);
        paged.Items.Should().NotContain(p => p.Id == provider.Id);
    }

    [TestMethod]
    public async Task ArchiveAsync_KeepsProviderResolvableByApiKey()
    {
        // The proxy's upstream-key auth resolves the provider by its API key; archiving must not
        // break that by-key lookup, so traffic with a matching key keeps being attributed.
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IModelProviderRepository>();
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);

        await repository.ArchiveAsync(provider.Id, CancellationToken);

        var byKey = await repository.FindByApiKeyAsync(provider.ApiKey, CancellationToken);
        byKey.Should().NotBeNull();
        byKey?.Id.Should().Be(provider.Id);
    }

    [TestMethod]
    public async Task ArchiveAsync_AlsoArchivesProvidersEndpoints()
    {
        // A hard provider delete would cascade-destroy its endpoints (and their AgentCall/TestRun
        // history). Archiving instead soft-archives the endpoints so they leave the pickers while the
        // history they back is preserved.
        IServiceProvider services = GetServices();
        var providerRepository = services.GetRequiredService<IModelProviderRepository>();
        var endpointRepository = services.GetRequiredService<IModelEndpointRepository>();
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);

        await providerRepository.ArchiveAsync(endpoint.Provider.Id, CancellationToken);

        var byProvider = await endpointRepository.GetByProviderAsync(endpoint.Provider.Id, CancellationToken);
        byProvider.Should().NotContain(e => e.Id == endpoint.Id);

        var stillResolvable = await endpointRepository.GetAsync(endpoint.Id, CancellationToken);
        stillResolvable.IsArchived.Should().BeTrue();
    }

    [TestMethod]
    public async Task ArchiveAsync_MissingProvider_ReturnsFalse()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IModelProviderRepository>();

        var archived = await repository.ArchiveAsync(Guid.NewGuid(), CancellationToken);

        archived.Should().BeFalse();
    }

    [TestMethod]
    public async Task ArchiveAsync_AlreadyArchived_ReturnsFalse()
    {
        // A repeated archive is a no-op and must report no state transition (false), so callers can
        // 404 and skip auditing rather than recording a phantom deletion on the second delete.
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IModelProviderRepository>();
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);

        var first = await repository.ArchiveAsync(provider.Id, CancellationToken);
        var second = await repository.ArchiveAsync(provider.Id, CancellationToken);

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [TestMethod]
    public async Task RemoveAsync_IsRefused_SoTracesAreNeverCascadeDeleted()
    {
        // Providers are archive-only: a hard delete would cascade through their endpoints to every
        // AgentCall (trace). RemoveAsync must refuse and the provider must remain.
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IModelProviderRepository>();
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);

        await FluentActions
            .Invoking(() => repository.RemoveAsync(provider.Id, CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();

        (await repository.ContainsAsync(provider.Id, CancellationToken)).Should().BeTrue();
    }
}
