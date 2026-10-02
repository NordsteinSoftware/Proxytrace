using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing;
using Proxytrace.Storage.Internal;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// The e2e per-test reset (<see cref="TestDataReset"/>) truncates a fixed table list with CASCADE.
/// A table that is neither in that list, nor reached by the CASCADE (which clears every table holding
/// a foreign key into a truncated one), survives every reset — and leaks one test's rows into the
/// next. That happened to sessions and scopes, whose ids are derived from (project, key): the "new"
/// scope of the next test was the previous test's row. Every table of the model must therefore be
/// classified, as content (cleared) or as setup baseline (kept on purpose).
/// </summary>
[TestClass]
public sealed class TestDataResetCoverageTests : BaseTest<Module>
{
    [TestMethod]
    public void EveryTable_IsEitherClearedByTheResetOrDeclaredBaseline()
    {
        IServiceProvider services = GetServices();
        var model = services.GetRequiredService<Func<StorageDbContext>>()().Model;
        var tables = model.GetEntityTypes()
            .Where(t => t.GetTableName() is not null)
            .ToDictionary(t => t, t => t.GetTableName() ?? string.Empty);

        // TRUNCATE ... CASCADE also truncates every table with a foreign key into a truncated table.
        var cleared = new HashSet<string>(TestDataReset.ContentTables);
        bool grew;
        do
        {
            grew = false;
            foreach (var (type, table) in tables)
            {
                if (!cleared.Contains(table)
                    && type.GetForeignKeys().Any(fk => cleared.Contains(fk.PrincipalEntityType.GetTableName() ?? string.Empty)))
                {
                    grew = cleared.Add(table);
                }
            }
        }
        while (grew);

        tables.Values.Except(cleared).Except(TestDataReset.BaselineTables).Should().BeEmpty(
            "every table must be cleared by the e2e reset or deliberately kept as setup baseline");
        TestDataReset.BaselineTables.Intersect(cleared).Should().BeEmpty(
            "a baseline table the CASCADE reaches would be wiped by every reset");
    }
}
