using Autofac;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nordstein.Core.Common.Security;
using Proxytrace.Domain;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Proxytrace.Storage.Internal;
using Nordstein.Core.Testing;
using ApiKeyEntity = Proxytrace.Storage.Internal.Entities.ApiKey.ApiKeyEntity;
using InviteEntity = Proxytrace.Storage.Internal.Entities.Invite.InviteEntity;
using ModelProviderEntity = Proxytrace.Storage.Internal.Entities.ModelProvider.ModelProviderEntity;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class SecretsBackfillServiceTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Backfill_ProtectsPlaintextRowsInPlace_AndIsIdempotent()
    {
        IServiceProvider services = GetServices();
        var contextFactory = services.GetRequiredService<Func<StorageDbContext>>();
        var now = DateTimeOffset.UtcNow;

        // Valid FK targets for the legacy ApiKey / Invite rows.
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var fkProvider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);
        var owner = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);

        // Seed pre-retrofit plaintext rows directly (bypassing the protect/hash-aware mappers).
        var providerId = Guid.NewGuid();
        var keyId = Guid.NewGuid();
        var inviteId = Guid.NewGuid();
        const string providerPlaintext = "sk-provider-plain";
        const string keyPlaintext = "proxytrace-inbound-plain";
        var tokenPlaintext = new string('a', 43); // base64url(32 bytes) length

        var seed = contextFactory();
        seed.Set<ModelProviderEntity>().Add(new ModelProviderEntity
        {
            Id = providerId, Name = "legacy-provider", Endpoint = "https://api.example.com/v1",
            ApiKey = providerPlaintext, ApiKeyLookupHash = null, Kind = ModelProviderKind.OpenAiCompatible,
            CreatedAt = now, UpdatedAt = now,
        });
        seed.Set<ApiKeyEntity>().Add(new ApiKeyEntity
        {
            Id = keyId, Name = "legacy-key", KeyHash = keyPlaintext, KeyPrefix = null,
            Project = project.Id, Provider = fkProvider.Id, Scopes = ApiKeyScopes.Ingestion, Owner = owner.Id,
            CreatedAt = now, UpdatedAt = now,
        });
        seed.Set<InviteEntity>().Add(new InviteEntity
        {
            Id = inviteId, Email = "legacy@example.com", Role = UserRole.Member, TokenHash = tokenPlaintext,
            ExpiresAt = now.AddDays(7), InvitedBy = owner.Id, CreatedAt = now, UpdatedAt = now,
        });
        await seed.SaveChangesAsync(CancellationToken);

        var backfill = services.GetRequiredService<SecretsBackfillService>();
        await backfill.StartAsync(CancellationToken);

        // Provider: encrypted in place, resolvable by its plaintext key via the blind index.
        var providers = services.GetRequiredService<IModelProviderRepository>();
        (await providers.FindByApiKeyAsync(providerPlaintext, CancellationToken))!.Id.Should().Be(providerId);
        var rawProvider = await contextFactory().Set<ModelProviderEntity>().AsNoTracking().FirstAsync(e => e.Id == providerId, CancellationToken);
        rawProvider.ApiKey.Should().NotBe(providerPlaintext);
        rawProvider.ApiKeyLookupHash.Should().NotBeNullOrEmpty();

        // Inbound key: hashed in place, resolvable by its raw value, prefix populated.
        var keys = services.GetRequiredService<IApiKeyRepository>();
        (await keys.FindByKeyAsync(keyPlaintext, CancellationToken))!.Id.Should().Be(keyId);
        var rawKey = await contextFactory().Set<ApiKeyEntity>().AsNoTracking().FirstAsync(e => e.Id == keyId, CancellationToken);
        rawKey.KeyHash.Should().NotBe(keyPlaintext);
        rawKey.KeyPrefix.Should().Be(keyPlaintext[..16]);

        // Invite: token hashed in place (now a 64-char hash).
        var rawInvite = await contextFactory().Set<InviteEntity>().AsNoTracking().FirstAsync(e => e.Id == inviteId, CancellationToken);
        rawInvite.TokenHash.Should().HaveLength(64).And.NotBe(tokenPlaintext);

        // Re-run is a no-op: already-protected rows are left untouched.
        var providerCipher = rawProvider.ApiKey;
        await backfill.StartAsync(CancellationToken);
        var rawProviderAgain = await contextFactory().Set<ModelProviderEntity>().AsNoTracking().FirstAsync(e => e.Id == providerId, CancellationToken);
        rawProviderAgain.ApiKey.Should().Be(providerCipher);
    }

    [TestMethod]
    public async Task Backfill_WhenPreRetrofitKeyWasSavedBeforeTheBackfillRan_StillHashesIt()
    {
        // Regression: the "not yet protected" marker for ApiKey used to be `KeyPrefix IS NULL`, but
        // ApiKeyConfig maps that null to string.Empty on read and writes the "" straight back, so any
        // save of a pre-retrofit row erased the marker and the row was skipped forever — its KeyHash
        // column kept the plaintext key, which FindByKeyAsync (which hashes the presented value) can
        // never match, so the credential silently stopped authenticating. The marker is now the shape
        // of KeyHash itself: a hex SHA-256 is 64 chars, a plaintext key is not.
        IServiceProvider services = GetServices();
        var contextFactory = services.GetRequiredService<Func<StorageDbContext>>();
        var now = DateTimeOffset.UtcNow;

        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var fkProvider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);
        var owner = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);

        var keyId = Guid.NewGuid();
        const string keyPlaintext = "proxytrace-inbound-plain";
        var seed = contextFactory();
        seed.Set<ApiKeyEntity>().Add(new ApiKeyEntity
        {
            Id = keyId, Name = "legacy-key", KeyHash = keyPlaintext, KeyPrefix = null,
            Project = project.Id, Provider = fkProvider.Id, Scopes = ApiKeyScopes.Ingestion, Owner = owner.Id,
            CreatedAt = now, UpdatedAt = now,
        });
        await seed.SaveChangesAsync(CancellationToken);

        // Round-trip the row through the mapper once — this is what destroys a NULL-based marker.
        var keys = services.GetRequiredService<IApiKeyRepository>();
        var loaded = await keys.GetAsync(keyId, CancellationToken);
        await keys.UpdateAsync(loaded, CancellationToken);
        var afterSave = await contextFactory().Set<ApiKeyEntity>().AsNoTracking().FirstAsync(e => e.Id == keyId, CancellationToken);
        afterSave.KeyPrefix.Should().BeEmpty("the mapper collapses the NULL, so KeyPrefix cannot be the marker");
        afterSave.KeyHash.Should().Be(keyPlaintext);

        await services.GetRequiredService<SecretsBackfillService>().StartAsync(CancellationToken);

        // The row is still recognised as un-backfilled and is hashed, so the key authenticates again.
        var found = await keys.FindByKeyAsync(keyPlaintext, CancellationToken);
        found.Should().NotBeNull();
        found?.Id.Should().Be(keyId);
        var raw = await contextFactory().Set<ApiKeyEntity>().AsNoTracking().FirstAsync(e => e.Id == keyId, CancellationToken);
        raw.KeyHash.Should().Be(Sha256.HexHash(keyPlaintext));
        raw.KeyPrefix.Should().Be(keyPlaintext[..16]);
    }

    [TestMethod]
    public async Task Backfill_WhenEncryptionFails_DoesNotCrash_AndLeavesProviderUnbackfilled()
    {
        // A protector that always throws stands in for a broken key ring. The provider pass must
        // degrade (the row stays plaintext/unmarked for a future retry) without crashing boot.
        var throwingProtector = Substitute.For<ISecretProtector>();
        throwingProtector.Protect(Arg.Any<string>()).Returns(_ => throw new InvalidOperationException("no key ring"));

        IServiceProvider services = GetServices(builder =>
            builder.RegisterInstance(throwingProtector).As<ISecretProtector>());
        var contextFactory = services.GetRequiredService<Func<StorageDbContext>>();
        var now = DateTimeOffset.UtcNow;

        var providerId = Guid.NewGuid();
        var seed = contextFactory();
        seed.Set<ModelProviderEntity>().Add(new ModelProviderEntity
        {
            Id = providerId, Name = "legacy", Endpoint = "https://api.example.com/v1",
            ApiKey = "sk-plain", ApiKeyLookupHash = null, Kind = ModelProviderKind.OpenAiCompatible,
            CreatedAt = now, UpdatedAt = now,
        });
        await seed.SaveChangesAsync(CancellationToken);

        var backfill = services.GetRequiredService<SecretsBackfillService>();
        // Must not throw, even though every encryption attempt fails.
        await backfill.StartAsync(CancellationToken);

        // The row is left untouched (marker still null) so a later boot can retry it.
        var raw = await contextFactory().Set<ModelProviderEntity>().AsNoTracking().FirstAsync(e => e.Id == providerId, CancellationToken);
        raw.ApiKey.Should().Be("sk-plain");
        raw.ApiKeyLookupHash.Should().BeNull();
    }
}
