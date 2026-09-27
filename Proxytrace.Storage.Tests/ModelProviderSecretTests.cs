using Autofac;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Domain;
using Proxytrace.Domain.ModelProvider;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Testing;
using System.Security.Cryptography;
using ModelProviderEntity = Proxytrace.Storage.Internal.Entities.ModelProvider.ModelProviderEntity;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class ModelProviderSecretTests : BaseTest<Module>
{
    [TestMethod]
    public async Task ApiKey_IsEncryptedAtRest_AndRoundTrips()
    {
        IServiceProvider services = GetServices();
        var providers = services.GetRequiredService<IModelProviderRepository>();
        var create = services.GetRequiredService<IModelProvider.CreateNew>();

        var saved = await providers.AddAsync(
            create("p", new Uri("https://api.example.com/v1"), "sk-secret-123", ModelProviderKind.OpenAiCompatible),
            CancellationToken);

        // The stored column holds ciphertext + a blind-index hash, never the plaintext.
        var context = services.GetRequiredService<Func<StorageDbContext>>()();
        var raw = await context.Set<ModelProviderEntity>().AsNoTracking().FirstAsync(e => e.Id == saved.Id, CancellationToken);
        raw.ApiKey.Should().NotBe("sk-secret-123");
        raw.ApiKeyLookupHash.Should().NotBeNullOrEmpty();

        // Loading decrypts back to the plaintext so it can be replayed upstream.
        var loaded = await providers.FindAsync(saved.Id, CancellationToken);
        loaded.Should().NotBeNull();
        loaded?.ApiKey.Should().Be("sk-secret-123");
    }

    [TestMethod]
    public async Task FindByApiKey_ResolvesByPlaintext_ViaBlindIndex()
    {
        IServiceProvider services = GetServices();
        var providers = services.GetRequiredService<IModelProviderRepository>();
        var create = services.GetRequiredService<IModelProvider.CreateNew>();

        var saved = await providers.AddAsync(
            create("p", new Uri("https://api.example.com/v1"), "sk-secret-456", ModelProviderKind.OpenAiCompatible),
            CancellationToken);

        var byKey = await providers.FindByApiKeyAsync("sk-secret-456", CancellationToken);
        byKey.Should().NotBeNull();
        byKey?.Id.Should().Be(saved.Id);
    }

    [TestMethod]
    public async Task FindByApiKey_ResolvesARowStillCarryingTheLegacyUnkeyedIndex()
    {
        // Rows written before the index became keyed carry a bare SHA-256 until the startup backfill
        // upgrades them. Accepting only the keyed form would make every provider on a not-yet-
        // backfilled install fail to authenticate upstream — an outage, not a hardening.
        IServiceProvider services = GetServices();
        var providers = services.GetRequiredService<IModelProviderRepository>();
        var create = services.GetRequiredService<IModelProvider.CreateNew>();
        var indexer = services.GetRequiredService<Domain.Security.ISecretIndexer>();

        var saved = await providers.AddAsync(
            create("p", new Uri("https://api.example.com/v1"), "sk-legacy-789", ModelProviderKind.OpenAiCompatible),
            CancellationToken);

        // Rewrite the index column to the pre-keying form, bypassing the mapper.
        var context = services.GetRequiredService<Func<StorageDbContext>>()();
        var raw = await context.Set<ModelProviderEntity>().FirstAsync(e => e.Id == saved.Id, CancellationToken);
        context.Entry(raw).CurrentValues.SetValues(raw with
        {
            ApiKeyLookupHash = indexer.LegacyIndex("sk-legacy-789"),
        });
        await context.SaveChangesAsync(CancellationToken);

        var byKey = await providers.FindByApiKeyAsync("sk-legacy-789", CancellationToken);

        byKey.Should().NotBeNull();
        byKey?.Id.Should().Be(saved.Id);
    }

    [TestMethod]
    public async Task FindByApiKey_WithAWrongKey_ResolvesNothing()
    {
        IServiceProvider services = GetServices();
        var providers = services.GetRequiredService<IModelProviderRepository>();
        var create = services.GetRequiredService<IModelProvider.CreateNew>();

        await providers.AddAsync(
            create("p", new Uri("https://api.example.com/v1"), "sk-right", ModelProviderKind.OpenAiCompatible),
            CancellationToken);

        (await providers.FindByApiKeyAsync("sk-wrong", CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public async Task FindAsync_WhenTheStoredKeyCannotBeDecrypted_LoadsTheProviderWithTheKeyUnset()
    {
        // A protector that always throws stands in for a lost Data Protection key ring. Reads must
        // degrade (docs/security.md): the provider loads with an empty key instead of failing entity
        // validation, so provider/agent/project listings keep rendering until the key is re-entered.
        var throwingProtector = Substitute.For<ISecretProtector>();
        throwingProtector.Unprotect(Arg.Any<string>())
            .Returns(_ => throw new CryptographicException("The key was not found in the key ring."));

        IServiceProvider services = GetServices(builder =>
            builder.RegisterInstance(throwingProtector).As<ISecretProtector>());
        var providers = services.GetRequiredService<IModelProviderRepository>();
        var contextFactory = services.GetRequiredService<Func<StorageDbContext>>();
        var now = DateTimeOffset.UtcNow;
        var providerId = Guid.NewGuid();

        var seed = contextFactory();
        seed.Set<ModelProviderEntity>().Add(new ModelProviderEntity
        {
            Id = providerId,
            Name = "orphaned",
            Endpoint = "https://api.example.com/v1",
            ApiKey = "ciphertext-from-another-key-ring",
            ApiKeyLookupHash = "hmac1:0000",
            Kind = ModelProviderKind.OpenAiCompatible,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await seed.SaveChangesAsync(CancellationToken);

        var loaded = await providers.FindAsync(providerId, CancellationToken);

        loaded.Should().NotBeNull();
        loaded?.ApiKey.Should().BeEmpty();
    }

    [TestMethod]
    public async Task UpdateAsync_WithAnEmptyKey_IsRefused_SoTheStoredCiphertextSurvives()
    {
        // Saving a provider whose key could not be decrypted without re-entering it would overwrite
        // the ciphertext with an empty value, destroying any chance of recovery once the key ring is
        // restored. The storage mapper refuses that write; the controller surfaces it as a 400.
        IServiceProvider services = GetServices();
        var providers = services.GetRequiredService<IModelProviderRepository>();
        var create = services.GetRequiredService<IModelProvider.CreateNew>();
        var createExisting = services.GetRequiredService<IModelProvider.CreateExisting>();

        var saved = await providers.AddAsync(
            create("p", new Uri("https://api.example.com/v1"), "sk-secret", ModelProviderKind.OpenAiCompatible),
            CancellationToken);
        var unavailable = createExisting(
            saved.Name, saved.Endpoint, string.Empty, saved.Kind, saved, apiKeyUnavailable: true);

        await FluentActions
            .Invoking(() => providers.UpdateAsync(unavailable, CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
