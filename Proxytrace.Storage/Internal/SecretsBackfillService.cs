using System.Security.Cryptography;
using System.Text.Json;
using Autofac.Features.OwnedInstances;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nordstein.Core.Common.Security;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.Security;
using Proxytrace.Storage.Internal.Entities.ApiKey;
using Proxytrace.Storage.Internal.Entities.Invite;
using Proxytrace.Storage.Internal.Entities.ModelProvider;

namespace Proxytrace.Storage.Internal;

/// <summary>
/// One-time, idempotent in-place protection of pre-retrofit plaintext secrets. Runs after the
/// database initializer (migrations applied) and before the app serves traffic. Each table carries a
/// per-row marker so a partial run resumes and a re-run is a no-op:
/// <list type="bullet">
/// <item><description><c>ModelProvider</c>: <c>ApiKeyLookupHash IS NULL</c> ⇒ <c>ApiKey</c> still plaintext.</description></item>
/// <item><description><c>ApiKey</c>: <c>KeyHash</c> length ≠ 64 ⇒ it still holds the plaintext key.</description></item>
/// <item><description><c>Invite</c>: a 64-char hash vs the 43-char base64url token ⇒ length 64 means done.</description></item>
/// </list>
/// It reads the still-plaintext value and writes the protected value directly (bypassing the
/// encrypt/hash-aware mappers). It never fails host boot: each table is isolated, and the provider
/// pass is skipped (logged) if encryption is unavailable, while the key-ring-independent hash passes
/// still run.
///
/// The two hash markers deliberately read the <em>protected column itself</em> rather than a
/// companion column: a hex SHA-256 is always 64 characters, while the pre-retrofit plaintexts are
/// not (an inbound key is <c>"proxytrace-"</c> + 43 base64url chars, an invite token 43). A marker
/// held in a separate column is only as durable as that column's mapper — <c>ApiKey.KeyPrefix</c>
/// was such a marker until its <c>NULL</c> turned out to be collapsed to <c>""</c> by
/// <c>ApiKeyConfig</c> (<c>stored.KeyPrefix ?? string.Empty</c>) on every round trip, so a single
/// save of an un-backfilled row would have hidden it from the pass forever, stranding a plaintext
/// key that can never authenticate again.
/// </summary>
internal sealed class SecretsBackfillService : IHostedService
{
    // Length of a hex-encoded SHA-256 — the shape of an already-protected verify-only column.
    private const int HexHashLength = 64;
    private const int DisplayPrefixLength = 16;

    // An Owned<StorageDbContext> factory (not the ambient-aware Func<StorageDbContext>): this service is a
    // singleton hosted service resolved from the root container, so the ambient factory's fresh-resolve
    // branch would track each pass's context on the root scope until process shutdown. Owned<> hands out a
    // context from a child lifetime scope each pass disposes instead (issue #256). The backfill never runs
    // inside a logical transaction, so it never needs the shared ambient context.
    private readonly Func<Owned<StorageDbContext>> contextFactory;
    private readonly ISecretProtector protector;
    private readonly ISecretIndexer indexer;
    private readonly ILogger<SecretsBackfillService> logger;
    private readonly ILogger<Audit> audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretsBackfillService"/> class.
    /// </summary>
    public SecretsBackfillService(
        Func<Owned<StorageDbContext>> contextFactory,
        ISecretProtector protector,
        ISecretIndexer indexer,
        ILogger<SecretsBackfillService> logger,
        ILogger<Audit> audit)
    {
        this.contextFactory = contextFactory;
        this.protector = protector;
        this.indexer = indexer;
        this.logger = logger;
        this.audit = audit;
    }

    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Starts asynchronously.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Until a row is backfilled its lookup column still holds the pre-retrofit plaintext, so the
        // hashed/encrypted lookup cannot match it — the impact text below spells out what stops
        // working if a pass keeps failing.
        var providerKeys = await RunSafely(BackfillProvidersAsync, "provider API key",
            "existing providers cannot authenticate upstream until this completes", cancellationToken);
        var inboundKeys = await RunSafely(BackfillApiKeysAsync, "inbound API key",
            "existing API keys cannot authenticate at the proxy or MCP server until this completes", cancellationToken);
        var inviteTokens = await RunSafely(BackfillInvitesAsync, "invite token",
            "pending invites cannot be redeemed until this completes", cancellationToken);
        var providerIndexes = await RunSafely(ReindexProviderKeysAsync, "provider key blind index",
            "provider keys stay recoverable from a database dump by wordlist until this completes",
            cancellationToken);

        // Audit the one-time at-rest protection only when it actually changed rows, so a re-run
        // (everything already protected) records nothing. No request context here ⇒ System actor.
        if (providerKeys + inboundKeys + inviteTokens + providerIndexes > 0)
        {
            audit.LogAudit(
                AuditAction.SecretsBackfilled, "Secrets",
                details: JsonSerializer.Serialize(new { providerKeys, inboundKeys, inviteTokens, providerIndexes }));
        }
    }

    /// <summary>
    /// Runs a single table's backfill with a few retries for transient faults. On persistent failure
    /// it logs at Critical (surfaced in the operator Error Log) with the operational impact, rather
    /// than failing host boot — but the impact is real, so the message is loud and actionable: a
    /// restart re-runs the backfill, and the affected credentials stay broken until it succeeds.
    /// </summary>
    private async Task<int> RunSafely(Func<CancellationToken, Task<int>> pass, string what, string impact, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await pass(cancellationToken);
            }
            catch (Exception ex) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Secrets backfill for {What} failed (attempt {Attempt}/{Max}); retrying.",
                    what, attempt, MaxAttempts);
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex,
                    "Secrets backfill for {What} failed after {Max} attempts — {Impact}. Restart to retry.",
                    what, MaxAttempts, impact);
                return 0;
            }
        }

        return 0;
    }

    private async Task<int> BackfillProvidersAsync(CancellationToken cancellationToken)
    {
        await using var owned = contextFactory();
        var db = owned.Value;
        var rows = await db.Set<ModelProviderEntity>()
            .Where(e => e.ApiKeyLookupHash == null)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            db.Entry(row).CurrentValues.SetValues(row with
            {
                ApiKey = protector.Protect(row.ApiKey),
                ApiKeyLookupHash = indexer.Index(row.ApiKey),
            });
        }

        if (rows.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Encrypted {Count} pre-existing provider API keys at rest.", rows.Count);
        }

        return rows.Count;
    }

    /// <summary>
    /// Upgrades provider rows still carrying the pre-keying, unkeyed blind index to the keyed one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The original index was a plain SHA-256, justified on the grounds that the secrets it covers
    /// are 256-bit CSPRNG values. That holds for the secrets Proxytrace generates, but not for this
    /// one: the upstream provider key is typed in by an operator, and self-hosted OpenAI-compatible
    /// backends conventionally use <c>EMPTY</c>, <c>ollama</c> or <c>sk-1234</c>. A database dump
    /// then recovers the key by wordlist, undoing the encryption on the column beside it.
    /// </para>
    /// <para>
    /// Re-indexing needs the plaintext, which means decrypting the stored ciphertext — so the pass is
    /// skipped when the key ring cannot decrypt, exactly like the encryption pass above. It is also
    /// skipped when no persisted blind-index key exists, because indexing under a per-process key
    /// would make every provider fail to authenticate after the next restart.
    /// </para>
    /// </remarks>
    private async Task<int> ReindexProviderKeysAsync(CancellationToken cancellationToken)
    {
        if (!indexer.IsKeyed)
        {
            // Not an error: Development and the test harnesses run without a data directory, and the
            // indexer already logged a warning naming the variable to set.
            return 0;
        }

        await using var owned = contextFactory();
        var db = owned.Value;

        // A row is upgraded when its index is present but not yet scheme-prefixed. Reading the
        // stored column itself, rather than a companion marker column, follows the same reasoning as
        // the two hash markers above: a separate marker is only as durable as its mapper.
        var rows = await db.Set<ModelProviderEntity>()
            .Where(e => e.ApiKeyLookupHash != null && !e.ApiKeyLookupHash.StartsWith(SecretIndexScheme.KeyedPrefix))
            .ToListAsync(cancellationToken);

        var upgraded = 0;
        foreach (var row in rows)
        {
            string plaintext;
            try
            {
                plaintext = protector.Unprotect(row.ApiKey);
            }
            catch (CryptographicException ex)
            {
                // An undecryptable row is already broken for upstream auth (see
                // ModelProviderConfig.Decrypt); re-indexing it to the HMAC of an empty string would
                // additionally make the legacy fallback stop matching. Leave it exactly as it is.
                logger.LogWarning(ex,
                    "Could not decrypt provider {ProviderId} while re-indexing its API key; leaving its "
                    + "existing blind index in place.", row.Id);
                continue;
            }

            db.Entry(row).CurrentValues.SetValues(row with { ApiKeyLookupHash = indexer.Index(plaintext) });
            upgraded++;
        }

        if (upgraded > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Re-indexed {Count} provider API keys under the keyed blind index.", upgraded);
        }

        return upgraded;
    }

    private async Task<int> BackfillApiKeysAsync(CancellationToken cancellationToken)
    {
        await using var owned = contextFactory();
        var db = owned.Value;
        // Marker: the stored KeyHash is not yet a hex SHA-256, so it is still the plaintext key.
        // KeyPrefix cannot serve as the marker — the mapper collapses its NULL to "" on any save.
        var rows = await db.Set<ApiKeyEntity>()
            .Where(e => e.KeyHash.Length != HexHashLength)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            var plaintext = row.KeyHash;
            db.Entry(row).CurrentValues.SetValues(row with
            {
                KeyHash = Sha256.HexHash(plaintext),
                KeyPrefix = plaintext.Length <= DisplayPrefixLength ? plaintext : plaintext[..DisplayPrefixLength],
            });
        }

        if (rows.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Hashed {Count} pre-existing inbound API keys at rest.", rows.Count);
        }

        return rows.Count;
    }

    private async Task<int> BackfillInvitesAsync(CancellationToken cancellationToken)
    {
        await using var owned = contextFactory();
        var db = owned.Value;
        var rows = await db.Set<InviteEntity>()
            .Where(e => e.TokenHash.Length != HexHashLength)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            db.Entry(row).CurrentValues.SetValues(row with { TokenHash = Sha256.HexHash(row.TokenHash) });
        }

        if (rows.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Hashed {Count} pre-existing invite tokens at rest.", rows.Count);
        }

        return rows.Count;
    }

    /// <summary>
    /// Stops asynchronously.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
