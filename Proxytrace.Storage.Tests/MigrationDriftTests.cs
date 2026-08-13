using Autofac;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// Guards against schema drift: a change to an EF model that no migration applies.
/// </summary>
/// <remarks>
/// EF's own <c>PendingModelChangesWarning</c> is suppressed in <c>Storage.Module</c> because it is
/// raised at <c>Migrate()</c> time — on a real deployment, against a real database, where throwing
/// means the application will not start. Suppressing it there and asserting the same condition here
/// keeps the safety net without letting a metadata quirk take down a production boot: drift is
/// caught in CI, by the people who can fix it, instead of at a customer's startup.
/// </remarks>
[TestClass]
public sealed class MigrationDriftTests : BaseTest<Module>
{
    [TestMethod]
    public void Model_HasNoChangesMissingFromMigrations()
    {
        // Needs the relational provider: the model differ compares against the migrations snapshot,
        // which only the Npgsql model carries. No connection is opened — nothing here talks to a
        // database, so this runs anywhere the unit suite runs.
        using var scope = BuildContainer(builder =>
            builder.RegisterInstance(StorageConfiguration.Postgres(
                    "Host=drift-check;Database=drift-check;Username=drift;Password=drift"))
                .As<StorageConfiguration>());

        using var context = scope.Resolve<StorageDbContext>();

        context.Database.HasPendingModelChanges().Should().BeFalse(
            "every model change must ship with the migration that applies it — run "
            + "`dotnet ef migrations add <Name> --project Proxytrace.Storage` and commit the result");
    }
}
