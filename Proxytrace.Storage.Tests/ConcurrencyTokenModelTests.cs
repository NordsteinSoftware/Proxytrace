using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Storage.Internal.Entities;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// Verifies that the optimistic-concurrency token (<c>UpdatedAt</c>) is wired into the EF model so a
/// real <c>WHERE UpdatedAt = @original</c> check is emitted on every update/delete. The in-memory
/// provider ignores concurrency tokens at runtime, so we assert on the model metadata rather than on
/// a save round-trip — the metadata is what drives the SQL generated against PostgreSQL.
/// </summary>
[TestClass]
public sealed class ConcurrencyTokenModelTests : BaseTest<Module>
{
    [TestMethod]
    public void EveryEntityWithUpdatedAt_HasItConfiguredAsConcurrencyToken()
    {
        IServiceProvider services = GetServices();
        using var context = services.GetRequiredService<StorageDbContext>();

        var entitiesWithUpdatedAt = context.Model
            .GetEntityTypes()
            .Where(e => e.FindProperty(nameof(Entity.UpdatedAt)) is not null)
            .ToArray();

        entitiesWithUpdatedAt.Should().NotBeEmpty(
            "every persisted entity derives from Entity and carries an UpdatedAt timestamp");

        foreach (var entityType in entitiesWithUpdatedAt)
        {
            var updatedAt = entityType.FindProperty(nameof(Entity.UpdatedAt));
            updatedAt.Should().NotBeNull();
            updatedAt?.IsConcurrencyToken.Should().BeTrue(
                "UpdatedAt on {0} must enforce optimistic concurrency at the database",
                entityType.DisplayName());
        }
    }
}
