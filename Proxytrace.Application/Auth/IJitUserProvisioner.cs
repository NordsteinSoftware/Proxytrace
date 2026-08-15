using Proxytrace.Domain.User;

namespace Proxytrace.Application.Auth;

/// <summary>
/// Represents a jit user provisioner.
/// </summary>
public interface IJitUserProvisioner
{
    Task<IUser> EnsureProvisionedAsync(
        string externalSubject,
        string email, 
        CancellationToken cancellationToken = default);
}
