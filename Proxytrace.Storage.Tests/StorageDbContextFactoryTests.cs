using AwesomeAssertions;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class StorageDbContextFactoryTests : BaseTest<Module>
{
    [TestMethod]
    public void CreateDbContext_WithPostgresAppSettings_BuildsContext()
    {
        var services = GetServices();
        using var tempDir = services.GetTempDirectory(prefix: "proxytrace-dbctx-");
        var prevCwd = Directory.GetCurrentDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(tempDir.Path, "appsettings.json"),
                """{"ConnectionStrings":{"Default":"Host=localhost;Port=5432;Database=proxytrace;Username=u;Password=p"}}""");
            Directory.SetCurrentDirectory(tempDir.Path);

            var factory = new StorageDbContextFactory();
            using var ctx = factory.CreateDbContext([]);

            ctx.Should().NotBeNull();
        }
        finally
        {
            Directory.SetCurrentDirectory(prevCwd);
        }
    }

    [TestMethod]
    public void CreateDbContext_MissingConnectionString_Throws()
    {
        var services = GetServices();
        using var tempDir = services.GetTempDirectory(prefix: "proxytrace-dbctx-");
        var prevCwd = Directory.GetCurrentDirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDir.Path, "appsettings.json"), "{}");
            Directory.SetCurrentDirectory(tempDir.Path);

            var factory = new StorageDbContextFactory();
            var act = () => factory.CreateDbContext([]);

            act.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            Directory.SetCurrentDirectory(prevCwd);
        }
    }
}
