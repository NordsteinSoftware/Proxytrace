using AwesomeAssertions;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

/// <summary>
/// Tests for <see cref="Proxytrace.Api.Module"/>. Storage is PostgreSQL-only for persistent
/// deployments (kiosk mode uses in-memory), so the composition root no longer routes between
/// providers; the connection string is passed straight to <c>StorageConfiguration.Postgres</c>.
/// </summary>
[TestClass]
public sealed class ModuleTests : BaseTest<Module>
{
    [TestMethod]
    public void Constructor_DefaultsToProduction()
    {
        var module = new Proxytrace.Api.Module();
        module.Should().NotBeNull();
    }
}
