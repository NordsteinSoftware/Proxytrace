using Proxytrace.Domain.UserTotpEnrollment;

namespace Proxytrace.Storage.Internal.Entities.UserTotpEnrollment;

[StoredDomainEntity(typeof(IUserTotpEnrollment))]
internal record UserTotpEnrollmentEntity : Entity
{
    /// <summary>
    /// Gets or sets the user.
    /// </summary>
    public required Guid User { get; init; }

    /// <summary>
    /// <see cref="IUserTotpEnrollment.Secret"/> — the Base32 TOTP shared secret, stored as
    /// non-deterministic ciphertext (encrypted via <c>ISecretProtector</c> in the mapper).
    /// </summary>
    public required string Secret { get; init; }

    /// <summary>
    /// Gets or sets the confirmed at.
    /// </summary>
    public DateTimeOffset? ConfirmedAt { get; init; }
    /// <summary>
    /// Gets or sets the last used step.
    /// </summary>
    public long? LastUsedStep { get; init; }
}
