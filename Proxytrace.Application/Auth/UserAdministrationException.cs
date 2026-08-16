namespace Proxytrace.Application.Auth;

/// <summary>
/// Raised when an admin action on a user violates a management invariant — e.g. demoting or
/// deleting the last remaining Admin, or an admin acting destructively on their own account.
/// Mapped to HTTP 409 Conflict by the API.
/// </summary>
public sealed class UserAdministrationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UserAdministrationException"/> class.
    /// </summary>
    public UserAdministrationException(string message) : base(message)
    {
    }
}
