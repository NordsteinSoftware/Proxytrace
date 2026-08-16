#if DEBUG
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Application.Auth.Local;
using Proxytrace.Application.Setup;
using Proxytrace.Domain.Demo;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Debug;

/// <summary>
/// DEBUG-ONLY developer back-door. On startup, seeds a fixed admin account
/// (<see cref="Email"/>) so a developer can always sign in to a local debug build through the
/// normal login form, without knowing the real admin password.
/// <para>
/// It deliberately does <em>not</em> short-circuit onboarding: <c>SetupService.AnyUsersExistAsync</c>
/// ignores this account (see <see cref="DebugBackDoorAccount"/>), so a fresh debug database still
/// reports <c>setupRequired</c> and shows the first-run wizard.
/// </para>
/// <para>
/// The entire type is compiled out of Release builds (<c>#if DEBUG</c>) and is registered only under
/// the same guard in <c>Program.cs</c>, so this credential never exists in a published/production
/// binary. Login still flows through the ordinary <c>LoginService</c> — this only ensures the user
/// row + password hash are present. Idempotent: it does nothing if the account already exists.
/// </para>
/// See <c>docs/debug_api.md</c>.
/// </summary>
internal sealed class DebugLoginSeederHostedService : IHostedService
{
    /// <summary>The fixed debug admin email. See <c>docs/debug_api.md</c>.</summary>
    internal const string Email = DebugBackDoorAccount.Email;

    /// <summary>The fixed debug admin password (hashed before storage, like any other user).</summary>
    internal const string Password = DebugBackDoorAccount.Password;

    private readonly IServiceProvider rootServices;
    private readonly ILogger<DebugLoginSeederHostedService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DebugLoginSeederHostedService"/> class.
    /// </summary>
    public DebugLoginSeederHostedService(
        IServiceProvider rootServices,
        ILogger<DebugLoginSeederHostedService> logger)
    {
        this.rootServices = rootServices;
        this.logger = logger;
    }

    /// <summary>
    /// Starts asynchronously.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = rootServices.CreateScope();

        var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
        await initializer.EnsureDatabaseReadyAsync(cancellationToken);

        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        if (await users.FindByEmailAsync(Email, cancellationToken) is not null)
        {
            logger.LogInformation("DEBUG build: debug admin {Email} already present", Email);
            return;
        }

        // Hash + persist exactly like SetupService.CreateFirstAdminAsync so the normal login path
        // verifies it. The hasher salts per user, so a placeholder draft is hashed first to obtain
        // the stored hash, then the real user is constructed with it.
        var createUser = scope.ServiceProvider.GetRequiredService<IUser.CreateNew>();
        var passwords = scope.ServiceProvider.GetRequiredService<IPasswordService>();

        var draft = createUser(Email, externalSubject: null, passwordHash: "placeholder", role: UserRole.Admin);
        var passwordHash = passwords.Hash(draft, Password);
        var user = createUser(Email, externalSubject: null, passwordHash: passwordHash, role: UserRole.Admin);
        await user.AddAsync(cancellationToken);

        logger.LogWarning(
            "DEBUG build: seeded developer back-door admin {Email} (compiled out of Release; see docs/debug_api.md)",
            Email);
    }

    /// <summary>
    /// Stops asynchronously.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
#endif
