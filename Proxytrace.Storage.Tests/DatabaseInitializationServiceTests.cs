using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Proxytrace.Domain.Demo;
using Proxytrace.Domain;
using Proxytrace.Domain.User;
using Proxytrace.Storage.Internal;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// Tests for <see cref="DatabaseInitializationService"/> against the in-memory provider.
/// Migration-based initialization (PostgreSQL) is exercised by the e2e suite.
/// </summary>
[TestClass]
public sealed class DatabaseInitializationServiceTests : BaseTest<Module>
{
    [TestMethod]
    public async Task StopAsync_CompletesSuccessfully()
    {
        var services = GetServices();
        var service = new DatabaseInitializationService(services, StorageConfiguration.InMemory(),
            NullLogger<DatabaseInitializationService>.Instance);

        await service.Invoking(s => s.StopAsync(CancellationToken)).Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task EnsureDatabaseReadyAsync_InMemory_CreatesUsableSchema()
    {
        var services = GetServices();
        var service = services.GetRequiredService<IDatabaseInitializer>();

        await service.EnsureDatabaseReadyAsync(CancellationToken);

        // Schema is usable: a round-trip through the repository succeeds.
        var generator = services.GetRequiredService<IDomainEntityGenerator<IUser>>();
        var repository = services.GetRequiredService<IRepository<IUser>>();

        IUser created = await generator.CreateAsync(CancellationToken);
        IUser retrieved = await repository.GetAsync(created.Id, CancellationToken);

        retrieved.Id.Should().Be(created.Id);
    }
}
