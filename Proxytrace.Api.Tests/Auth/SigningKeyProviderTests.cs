using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Proxytrace.Api.Auth;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests.Auth;

[TestClass]
public sealed class SigningKeyProviderTests : BaseTest<Module>
{
    private const string ValidConfiguredKey = "preset-key-1234-preset-key-1234-abcd";

    [TestMethod]
    public void EnsureSigningKey_ConfiguredValue_IsReturnedAsIs()
    {
        var services = GetServices();
        var provider = services.GetRequiredService<ISigningKeyProvider>();

        var result = provider.EnsureSigningKey(ValidConfiguredKey);

        result.Should().Be(ValidConfiguredKey);
    }

    [TestMethod]
    public void EnsureSigningKey_ConfiguredValueTooShort_Throws()
    {
        var services = GetServices();
        var provider = services.GetRequiredService<ISigningKeyProvider>();

        FluentActions
            .Invoking(() => provider.EnsureSigningKey("too-short"))
            .Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void EnsureSigningKey_NoConfig_GeneratesAndPersistsKey()
    {
        var services = GetServices();
        var host = services.GetRequiredService<IHostEnvironment>();
        var dir = host.ContentRootPath;
        try
        {
            var provider = services.GetRequiredService<ISigningKeyProvider>();
            var first = provider.EnsureSigningKey(configured: null);

            first.Should().NotBeNullOrWhiteSpace();
            File.Exists(Path.Combine(dir, "appsettings.local.json")).Should().BeTrue();
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [TestMethod]
    public void EnsureSigningKey_NoConfig_ReusesPreviouslyPersistedKey()
    {
        // Sessions must survive restarts: a second resolution without configuration loads the
        // persisted key instead of generating a fresh one.
        var services = GetServices();
        var host = services.GetRequiredService<IHostEnvironment>();
        var dir = host.ContentRootPath;
        try
        {
            var provider = services.GetRequiredService<ISigningKeyProvider>();
            var first = provider.EnsureSigningKey(configured: null);
            var second = provider.EnsureSigningKey(configured: null);

            second.Should().Be(first);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [TestMethod]
    public void DataDirectoryStore_PersistAndLoad_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"proxytrace-test-{Guid.NewGuid():N}");
        try
        {
            var store = new DataDirectorySigningKeyStore(dir);
            store.Load().Should().BeNull();

            store.Persist("the-signing-key");

            new DataDirectorySigningKeyStore(dir).Load().Should().Be("the-signing-key");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [TestMethod]
    public void EnsureSigningKey_NoConfig_ExistingFileWithComments_DoesNotThrow()
    {
        var services = GetServices();
        var env = services.GetRequiredService<IHostEnvironment>();
        var dir = env.ContentRootPath;
        var path = Path.Combine(dir, "appsettings.local.json");
        try
        {
            File.WriteAllText(path, """
                {
                    // a JSONC comment that ASP.NET's config provider tolerates
                    "SomeOther": { "Value": "keep-me" },
                }
                """);

            var provider = services.GetRequiredService<ISigningKeyProvider>();
            var generated = provider.EnsureSigningKey(configured: null);

            generated.Should().NotBeNullOrWhiteSpace();
            var json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            json?["SomeOther"]?["Value"]?.GetValue<string>().Should().Be("keep-me");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [TestMethod]
    public void EnsureSigningKey_NoConfig_PreservesExistingLocalSettings()
    {
        var services = GetServices();
        var env = services.GetRequiredService<IHostEnvironment>();
        var dir = env.ContentRootPath;
        var path = Path.Combine(dir, "appsettings.local.json");
        try
        {
            File.WriteAllText(path, """{"SomeOther":{"Value":"keep-me"}}""");

            var provider = services.GetRequiredService<ISigningKeyProvider>();
            var generated = provider.EnsureSigningKey(configured: null);

            var json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            json.Should().NotBeNull();
            json?["SomeOther"]?["Value"]?.GetValue<string>().Should().Be("keep-me");
            json?["Authentication"]?["Local"]?["SigningKey"]?.GetValue<string>().Should().Be(generated);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }
}
