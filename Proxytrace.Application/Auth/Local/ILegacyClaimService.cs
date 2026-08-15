namespace Proxytrace.Application.Auth.Local;

/// <summary>
/// Service that provides legacy claim functionality.
/// </summary>
public interface ILegacyClaimService
{
    Task<bool> IsClaimAvailableAsync(CancellationToken cancellationToken = default);

    Task<LoginResult?> ClaimAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);
}
