namespace Proxytrace.Application.Auth.Local;

/// <summary>
/// Handles the one-time first-admin claim flow for installations bootstrapped with an auto-generated token instead of the setup wizard.
/// </summary>
public interface ILegacyClaimService
{
    /// <summary>
    /// Returns true when the installation has an unclaimed bootstrap token and no admin has been set up yet.
    /// </summary>
    Task<bool> IsClaimAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the bootstrap token, creates the first admin account, and returns a session token; null when the token is invalid or already claimed.
    /// </summary>
    Task<LoginResult?> ClaimAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);
}
