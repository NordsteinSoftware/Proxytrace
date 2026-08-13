using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Storage.Internal.Entities.AgentCall;
using Proxytrace.Storage.Internal.Entities.ApiKey;
using Proxytrace.Storage.Internal.Entities.ModelEndpoint;
using Proxytrace.Storage.Internal.Entities.ModelProvider;
using Proxytrace.Storage.Internal.Entities.TestRun;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// Guards the foreign keys that protect the high-volume <see cref="AgentCallEntity"/> traces table
/// from a cascade delete. A <c>Cascade</c> on either FK let a single hard delete of a config row
/// (a <c>ModelEndpoint</c> or, transitively, a <c>ModelProvider</c>) wipe every trace recorded
/// against it — irreversible telemetry loss (issue #191). Endpoints/providers are removed via the
/// archive flow, never hard-deleted, so these FKs must be <c>Restrict</c>.
/// <para>
/// The in-memory provider does not enforce <c>Restrict</c>/<c>Cascade</c> at runtime, so we assert
/// on the EF model metadata — which is what drives the SQL generated against PostgreSQL — rather
/// than on a delete round-trip. Same approach as <see cref="ConcurrencyTokenModelTests"/>.
/// </para>
/// </summary>
[TestClass]
public sealed class CascadeDeleteBehaviorModelTests : BaseTest<Module>
{
    [TestMethod]
    public void AgentCallToModelEndpoint_IsRestrict_SoDeletingAnEndpointCannotWipeTraces()
    {
        DeleteBehaviorFor<AgentCallEntity>(nameof(AgentCallEntity.EndpointId))
            .Should().Be(DeleteBehavior.Restrict,
                "a hard delete of a ModelEndpoint must never cascade-delete its AgentCall traces");
    }

    [TestMethod]
    public void ModelEndpointToModelProvider_IsRestrict_SoDeletingAProviderCannotWipeTraces()
    {
        DeleteBehaviorFor<ModelEndpointEntity>(nameof(ModelEndpointEntity.Provider))
            .Should().Be(DeleteBehavior.Restrict,
                "a hard delete of a ModelProvider must never cascade through its endpoints to the traces table");
    }

    [TestMethod]
    public void ApiKeyToModelProvider_IsRestrict_SoDeletingAProviderCannotDestroyItsKeys()
    {
        // Only the key's hash is stored, so a deleted API key is unrecoverable — it cannot be
        // re-issued, only replaced, and every integration configured with it breaks at once with
        // nothing but a 401 to go on.
        DeleteBehaviorFor<ApiKeyEntity>(nameof(ApiKeyEntity.Provider))
            .Should().Be(DeleteBehavior.Restrict,
                "a hard delete of a ModelProvider must never cascade-delete the API keys issued for it");
    }

    [TestMethod]
    public void ApiKeyToUser_IsRestrict_SoOffboardingAnOwnerCannotDestroyTheirKeys()
    {
        // Deleting a user used to silently revoke every key they had minted. The keys are
        // unrecoverable, so that turned routine offboarding into an unannounced outage;
        // UserAdministrationService now refuses the delete with a 409 naming the keys.
        DeleteBehaviorFor<ApiKeyEntity>(nameof(ApiKeyEntity.Owner))
            .Should().Be(DeleteBehavior.Restrict,
                "deleting a user must never cascade-delete the API keys they own");
    }

    [TestMethod]
    public void TestRunToModelEndpoint_IsRestrict_SoDeletingAnEndpointCannotWipeTestRuns()
    {
        DeleteBehaviorFor<TestRunEntity>(nameof(TestRunEntity.Endpoint))
            .Should().Be(DeleteBehavior.Restrict,
                "a hard delete of a ModelEndpoint must never cascade-delete its TestRun history");
    }

    [TestMethod]
    public void AgentCallToolToAgentCall_IsCascade_SoDeletingATraceRemovesItsToolRows()
    {
        // The opposite direction from the FKs above: AgentCallToolEntity is an owned child
        // projection (one row per distinct requested tool name), so it must NOT outlive its
        // parent trace as an orphan row. Asserted on model metadata for the same reason as the
        // Restrict FKs above — AbstractRepository.RemoveAsync loads the parent by primary key only
        // (no Include of Tools), so the in-memory provider's client-side cascade (which only
        // touches currently-tracked entities) never fires; PostgreSQL enforces this at the database
        // via the real `ON DELETE CASCADE`, which only the model metadata — not a round-trip
        // delete against the in-memory provider — can verify here.
        DeleteBehaviorFor<AgentCallToolEntity>(nameof(AgentCallToolEntity.AgentCallId))
            .Should().Be(DeleteBehavior.Cascade,
                "a deleted trace must not leave orphaned tool-name rows behind");
    }

    private DeleteBehavior DeleteBehaviorFor<TEntity>(string foreignKeyPropertyName)
    {
        IServiceProvider services = GetServices();
        using var context = services.GetRequiredService<StorageDbContext>();

        var entityType = context.Model.FindEntityType(typeof(TEntity));
        entityType.Should().NotBeNull("{0} must be mapped", typeof(TEntity).Name);

        var foreignKey = entityType?
            .GetForeignKeys()
            .SingleOrDefault(fk => fk.Properties.Any(p => p.Name == foreignKeyPropertyName));
        foreignKey.Should().NotBeNull(
            "{0} must declare a foreign key on {1}", typeof(TEntity).Name, foreignKeyPropertyName);

        return foreignKey?.DeleteBehavior ?? DeleteBehavior.Cascade;
    }
}
