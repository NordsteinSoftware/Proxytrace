using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class ModelEndpointArchiveTests : BaseTest<Module>
{
    [TestMethod]
    public async Task ManualPricing_RoundTripsAllPrices_AndCanReturnToAutomatic()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IModelEndpointRepository>();
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);
        endpoint.ManualPricing.Should().BeFalse();
        var update = services.GetRequiredService<IModelEndpoint.CreateExisting>();
        await repository.UpdateAsync(update(endpoint.Model, endpoint.Provider, 3.123456m, 0, null, endpoint, true), CancellationToken);
        // Provider-scoped reads reconstitute the entity from storage instead of returning the cached domain object.
        var stored = (await repository.GetByProviderAsync(endpoint.Provider.Id, CancellationToken)).Single(e => e.Id == endpoint.Id);
        stored.ManualPricing.Should().BeTrue();
        stored.InputTokenCost.Should().Be(3.123456m);
        stored.OutputTokenCost.Should().Be(0);
        stored.CachedInputTokenCost.Should().BeNull();
        await repository.UpdateAsync(update(stored.Model, stored.Provider, 3, 4, 1, stored, false), CancellationToken);
        stored = (await repository.GetByProviderAsync(endpoint.Provider.Id, CancellationToken)).Single(e => e.Id == endpoint.Id);
        stored.ManualPricing.Should().BeFalse();
        stored.CachedInputTokenCost.Should().Be(1);
    }

    [TestMethod]
    public async Task ArchiveAsync_ExcludesEndpointFromGetByProvider()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IModelEndpointRepository>();
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);

        await repository.ArchiveAsync(endpoint.Id, CancellationToken);

        var results = await repository.GetByProviderAsync(endpoint.Provider.Id, CancellationToken);
        results.Should().NotContain(e => e.Id == endpoint.Id);
    }

    [TestMethod]
    public async Task ArchiveAsync_ExcludesEndpointFromGetAll()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IModelEndpointRepository>();
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);

        await repository.ArchiveAsync(endpoint.Id, CancellationToken);

        var all = await repository.GetAllAsync(CancellationToken);
        all.Should().NotContain(e => e.Id == endpoint.Id);
    }

    [TestMethod]
    public async Task ArchiveAsync_KeepsEndpointResolvableById()
    {
        // Past proposals/theories and agents live-fetch the endpoint by id, so it must still resolve.
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IModelEndpointRepository>();
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);

        await repository.ArchiveAsync(endpoint.Id, CancellationToken);

        var retrieved = await repository.GetAsync(endpoint.Id, CancellationToken);
        retrieved.Id.Should().Be(endpoint.Id);
        retrieved.IsArchived.Should().BeTrue();
    }

    [TestMethod]
    public async Task ArchiveAsync_MissingEndpoint_ReturnsFalse()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IModelEndpointRepository>();

        var archived = await repository.ArchiveAsync(Guid.NewGuid(), CancellationToken);

        archived.Should().BeFalse();
    }

    [TestMethod]
    public async Task RemoveAsync_IsRefused_SoTracesAreNeverCascadeDeleted()
    {
        // Endpoints are archive-only: a hard delete would cascade-remove every AgentCall recorded
        // against them. RemoveAsync must refuse and the endpoint must remain.
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IModelEndpointRepository>();
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);

        await FluentActions
            .Invoking(() => repository.RemoveAsync(endpoint.Id, CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();

        (await repository.ContainsAsync(endpoint.Id, CancellationToken)).Should().BeTrue();
    }
}
