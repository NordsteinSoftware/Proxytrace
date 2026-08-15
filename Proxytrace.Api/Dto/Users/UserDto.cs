using Proxytrace.Domain.Notification;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Dto.Users;

/// <summary>
/// Data transfer object representing a user.
/// </summary>
public record UserDto(
    Guid Id,
    string Email,
    UserRole Role,
    bool IsExternal,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool MfaEnabled);

/// <summary>
/// Request payload for update user role operations.
/// </summary>
public record UpdateUserRoleRequest(UserRole Role);

/// <summary>Self-service UI language change for the current user (BCP-47 culture code).</summary>
public record UpdateMyLanguageRequest(string Language);

/// <summary>Lightweight project reference for the user-centric project assignment editor.</summary>
public record UserProjectDto(Guid Id, string Name);

/// <summary>Self-service email-notification preferences for the current user.</summary>
public record UpdateMyEmailNotificationsRequest(bool Enabled, NotificationSeverity MinSeverity);

/// <summary>
/// An admin-minted, one-time password-reset link for a user, plus when it expires. The link is shown
/// once and cannot be reconstructed afterwards (the token is stored only as a hash).
/// </summary>
public record ResetLinkResponse(string Link, DateTimeOffset ExpiresAt);
