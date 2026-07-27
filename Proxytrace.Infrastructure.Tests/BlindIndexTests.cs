using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Common.Security;
using Proxytrace.Domain.Security;
using Proxytrace.Infrastructure.Security;
using Proxytrace.Testing;

namespace Proxytrace.Infrastructure.Tests;

/// <summary>
/// The blind index over the upstream provider API key must be <b>keyed</b>.
/// </summary>
/// <remarks>
/// The unkeyed SHA-256 used by <see cref="ISecretHasher"/> is safe for the secrets Proxytrace
/// generates itself — 256-bit CSPRNG values a dump cannot reverse. The provider key is not one of
/// those: an operator types it in, and self-hosted OpenAI-compatible backends conventionally use
/// <c>EMPTY</c>, <c>ollama</c> or <c>sk-1234</c>. Unkeyed, a database dump recovers those by wordlist
/// in seconds, undoing the encryption on the column beside it.
/// </remarks>
[TestClass]
public sealed class BlindIndexTests : BaseTest<Module>
{
    [TestMethod]
    public void Index_WithAPersistedKey_IsNotDerivableFromTheValueAlone()
    {
        // The whole point: the index must not be reproducible by someone holding only the dump.
        using var dataDir = new TempDataDirectory();
        var indexer = ResolveIndexer(dataDir.Path);

        indexer.IsKeyed.Should().BeTrue();

        const string weakKey = "ollama";
        var index = indexer.Index(weakKey);

        index.Should().NotContain(Sha256.HexHash(weakKey),
            "an attacker with the dump can compute the plain hash of every wordlist entry");
        index.Should().StartWith(SecretIndexScheme.KeyedPrefix, "the scheme must be self-describing");
    }

    [TestMethod]
    public void Index_IsStableAcrossInstancesSharingADataDirectory()
    {
        // The API host writes the index and the lean proxy host reads it. If the two derived
        // different keys, every provider-key lookup at the proxy would miss.
        using var dataDir = new TempDataDirectory();

        var first = ResolveIndexer(dataDir.Path).Index("sk-shared");
        var second = ResolveIndexer(dataDir.Path).Index("sk-shared");

        second.Should().Be(first);
    }

    [TestMethod]
    public void Index_UnderDifferentDataDirectories_Differs()
    {
        // Two unrelated installations must not produce the same index for the same weak key —
        // otherwise a precomputed table built against one works against all of them.
        using var a = new TempDataDirectory();
        using var b = new TempDataDirectory();

        ResolveIndexer(a.Path).Index("ollama").Should().NotBe(ResolveIndexer(b.Path).Index("ollama"));
    }

    [TestMethod]
    public void Index_WithNoDataDirectory_FallsBackToTheLegacyIndexRatherThanAnEphemeralKey()
    {
        // A per-process random key would make every stored index stop matching on the next restart,
        // silently breaking upstream authentication for every provider. Falling back keeps
        // Development and the test harnesses working exactly as before.
        var indexer = ResolveIndexer(keyRingPath: null);

        indexer.IsKeyed.Should().BeFalse();
        indexer.Index("ollama").Should().Be(indexer.LegacyIndex("ollama"));
    }

    [TestMethod]
    public void LegacyIndex_MatchesThePreKeyingScheme()
    {
        // This is what rows written before the change carry; the lookup still has to match them.
        using var dataDir = new TempDataDirectory();

        ResolveIndexer(dataDir.Path).LegacyIndex("sk-old").Should().Be(Sha256.HexHash("sk-old"));
    }

    private ISecretIndexer ResolveIndexer(string? keyRingPath)
    {
        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterModule<SecretProtectionModule>();
            builder.RegisterInstance(new KeyRingLocation(keyRingPath));
        });
        return services.GetRequiredService<ISecretIndexer>();
    }

    private sealed class TempDataDirectory : IDisposable
    {
        public TempDataDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "proxytrace-blind-index-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best effort — a leaked temp directory must not fail the suite.
            }
        }
    }
}
