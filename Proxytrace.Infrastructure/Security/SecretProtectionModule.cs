using Autofac;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nordstein.Core.Common.DependencyInjection;
using Proxytrace.Domain.Security;

namespace Proxytrace.Infrastructure.Security;

/// <summary>
/// Registers the at-rest secret seams (<see cref="ISecretProtector"/> and <see cref="ISecretHasher"/>)
/// together with the ASP.NET Core Data Protection key ring they sit on.
///
/// Shared by every host that reads or writes protected secrets — the main API host and the lean
/// ingestion proxy host — so both resolve the <em>same</em> key ring: application name "Proxytrace",
/// persisted to <c>PROXYTRACE_DATA_DIR/dataprotection-keys</c>. The proxy must decrypt the upstream
/// provider key that the API encrypted, which only works when both processes load an identical
/// key-ring configuration; keeping that configuration in one module stops the two hosts from silently
/// drifting (a mismatched application name or key path would make decryption fail at runtime). Both
/// hosts must therefore also mount the same <c>PROXYTRACE_DATA_DIR</c> volume. See <c>docs/security.md</c>.
///
/// When the variable is unset the ring stays in memory and dies with the process; that is legitimate
/// in Development and in the test harnesses, so it is not fatal — but it is silently destructive in a
/// real deployment, so <see cref="KeyRingPersistenceCheck"/> shouts about it at startup.
/// </summary>
public sealed class SecretProtectionModule : Autofac.Module
{
    /// <summary>
    /// Environment variable naming the writable directory that holds the Data Protection key ring.
    /// </summary>
    public const string DataDirectoryVariable = "PROXYTRACE_DATA_DIR";

    private const string KeyRingDirectoryName = "dataprotection-keys";

    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder.RegisterType<Internal.DataProtectionSecretProtector>()
            .As<ISecretProtector>()
            .SingleInstance();

        builder.RegisterType<Internal.Sha256SecretHasher>()
            .As<ISecretHasher>()
            .SingleInstance();

        // Blind index for the operator-entered upstream provider key. Keyed (HMAC), unlike
        // ISecretHasher — that one covers 256-bit CSPRNG secrets a dump cannot reverse, while a
        // provider key is whatever the operator typed, often "EMPTY" or "ollama" on a self-hosted
        // backend. See ISecretIndexer.
        builder.RegisterType<Internal.BlindIndexKey>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<Internal.HmacSecretIndexer>()
            .As<ISecretIndexer>()
            .SingleInstance();

        var dataProtectionDir = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        builder.RegisterServiceCollection(services =>
        {
            var dataProtection = services.AddDataProtection().SetApplicationName("Proxytrace");
            if (!string.IsNullOrWhiteSpace(dataProtectionDir))
            {
                dataProtection.PersistKeysToFileSystem(
                    new DirectoryInfo(Path.Combine(dataProtectionDir, KeyRingDirectoryName)));
            }
        });

        // The resolved location is registered as a value so the startup check reports on exactly the
        // configuration applied above, and so a test can exercise both branches without mutating
        // process-wide environment state.
        builder.RegisterInstance(new KeyRingLocation(
                string.IsNullOrWhiteSpace(dataProtectionDir)
                    ? null
                    : Path.Combine(dataProtectionDir, KeyRingDirectoryName)))
            .SingleInstance();

        // Resolvable as itself so tests can drive it directly, mirroring the storage backfills.
        builder.RegisterType<KeyRingPersistenceCheck>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterServiceCollection(services =>
            services.AddHostedService(sp => sp.GetRequiredService<KeyRingPersistenceCheck>()));
    }
}

/// <summary>
/// Where the Data Protection key ring is persisted, or <c>null</c> when it is in-memory only because
/// <see cref="SecretProtectionModule.DataDirectoryVariable"/> was unset.
/// </summary>
internal sealed record KeyRingLocation(string? KeyRingPath);

/// <summary>
/// Startup check that makes an ephemeral Data Protection key ring loud instead of silent.
///
/// Without a persisted ring every encrypted secret — upstream provider API keys, TOTP/MFA
/// enrollments, the SMTP password — becomes undecryptable the moment the process restarts, and the
/// three decrypt paths degrade a <c>CryptographicException</c> to an empty secret at Warning only. An
/// operator writing their own manifest therefore gets a green boot whose only symptoms are upstream
/// 401s and "invalid authenticator code", with nothing at Error level. This logs at Critical so it
/// reaches the operator Error Log (which captures <c>&gt;= Error</c>), but does not throw: Development
/// and the test harnesses legitimately run without a data directory.
/// </summary>
internal sealed class KeyRingPersistenceCheck : IHostedService
{
    private readonly KeyRingLocation location;

    // Categorised under the (public) module rather than this internal class: the category is what an
    // operator filters the log by, so it must be a stable, nameable type — and NSubstitute cannot
    // proxy an ILogger<T> whose T is internal, which would make this unassertable in a test.
    private readonly ILogger<SecretProtectionModule> logger;

    public KeyRingPersistenceCheck(KeyRingLocation location, ILogger<SecretProtectionModule> logger)
    {
        this.location = location;
        this.logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (location.KeyRingPath is null)
        {
            logger.LogCritical(
                "{Variable} is not set, so the Data Protection key ring is held in memory and is discarded when "
                + "this process stops. Every secret encrypted at rest — upstream provider API keys, TOTP/MFA "
                + "enrollments and the SMTP password — will fail to decrypt after a restart and is silently read "
                + "back as unset, which shows up only as upstream 401s and rejected authenticator codes. Set "
                + "{Variable} to a persistent, writable directory (the official images and compose files use "
                + "/app/data) and point every host that reads or writes secrets at the same volume. See "
                + "docs/security.md.",
                SecretProtectionModule.DataDirectoryVariable,
                SecretProtectionModule.DataDirectoryVariable);
        }
        else
        {
            logger.LogInformation("Data Protection key ring persisted to {KeyRingPath}.", location.KeyRingPath);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
