using Proxytrace.Domain.User;

namespace Proxytrace.Application.Auth;

/// <summary>
/// Creates or returns the local user record for an OIDC subject on first login, handling the just-in-time provisioning flow.
/// </summary>
public interface IJitUserProvisioner
{
    /// <summary>
    /// Returns the user for the given OIDC subject, creating a local record on first login.
    /// </summary>
    Task<IUser> EnsureProvisionedAsync(
        string externalSubject,
        string email,
        CancellationToken cancellationToken = default);
}
