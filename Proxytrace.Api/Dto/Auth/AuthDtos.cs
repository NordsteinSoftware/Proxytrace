using Proxytrace.Domain.Notification;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Dto.Auth;

/// <summary>
/// Data transfer object representing a auth mode.
/// </summary>
public record AuthModeDto(string Mode, bool SetupRequired, bool LegacyClaimAvailable);
/// <summary>
/// Request payload for login operations.
/// </summary>
public record LoginRequest(string Email, string Password);
/// <summary>
/// Request payload for claim legacy operations.
/// </summary>
public record ClaimLegacyRequest(string Email, string Password);
/// <summary>
/// Request payload for signup operations.
/// </summary>
public record SignupRequest(string Token, string Password);
/// <summary>
/// Request payload for setup admin operations.
/// </summary>
public record SetupAdminRequest(string Email, string Password);
/// <summary>
/// Response payload for token operations.
/// </summary>
public record TokenResponse(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Login / reset-completion result. Either a session was issued (<see cref="Token"/> set,
/// <see cref="MfaRequired"/> false) or a second factor is required (<see cref="MfaRequired"/> true,
/// <see cref="MfaChallengeToken"/> set) — complete it via <c>POST /api/auth/mfa/verify</c>.
/// </summary>
public record LoginResponseDto(
    string? Token,
    DateTimeOffset? ExpiresAt,
    bool MfaRequired,
    string? MfaChallengeToken,
    DateTimeOffset? MfaChallengeExpiresAt);
/// <summary>
/// Request payload for mfa verify operations.
/// </summary>
public record MfaVerifyRequest(string ChallengeToken, string Code);
/// <summary>
/// Request payload for mfa activate operations.
/// </summary>
public record MfaActivateRequest(string Code);
/// <summary>
/// Request payload for mfa disable operations.
/// </summary>
public record MfaDisableRequest(string Password);
/// <summary>
/// Response payload for mfa setup operations.
/// </summary>
public record MfaSetupResponse(string Secret, string OtpAuthUri);
/// <summary>
/// Response payload for mfa activate operations.
/// </summary>
public record MfaActivateResponse(IReadOnlyList<string> BackupCodes);
/// <summary>
/// Data transfer object representing a me.
/// </summary>
public record MeDto(
    Guid Id,
    string Email,
    UserRole Role,
    string Language,
    bool EmailNotificationsEnabled,
    NotificationSeverity EmailNotificationMinSeverity,
    bool EmailEnabled,
    bool MfaEnabled);
/// <summary>
/// Response payload for stream ticket operations.
/// </summary>
public record StreamTicketResponse(string Token, DateTimeOffset ExpiresAt);
/// <summary>
/// Request payload for forgot password operations.
/// </summary>
public record ForgotPasswordRequest(string Email);
/// <summary>
/// Request payload for reset password operations.
/// </summary>
public record ResetPasswordRequest(string Token, string Password);
/// <summary>
/// Request payload for create invite operations.
/// </summary>
public record CreateInviteRequest(string Email, UserRole Role);
/// <summary>
/// Data transfer object representing a invite.
/// </summary>
public record InviteDto(Guid Id, string Email, UserRole Role, DateTimeOffset ExpiresAt, DateTimeOffset? ConsumedAt);
/// <summary>
/// Response payload for create invite operations.
/// </summary>
public record CreateInviteResponse(string Token, string Url, DateTimeOffset ExpiresAt);
/// <summary>
/// Data transfer object representing a invite preview.
/// </summary>
public record InvitePreviewDto(string Email, UserRole Role, DateTimeOffset ExpiresAt);
