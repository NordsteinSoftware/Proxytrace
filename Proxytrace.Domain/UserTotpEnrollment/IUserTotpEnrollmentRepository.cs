namespace Proxytrace.Domain.UserTotpEnrollment;

/// <summary>
/// Repository for persisting and querying user totp enrollment entities.
/// </summary>
public interface IUserTotpEnrollmentRepository : IRepository<IUserTotpEnrollment>
{
    /// <summary>The user's TOTP enrollment (pending or confirmed), or <see langword="null"/> if none exists.</summary>
    Task<IUserTotpEnrollment?> FindByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The ids of all users with a confirmed (enforced) enrollment. One query, for list views.</summary>
    Task<IReadOnlyCollection<Guid>> ListConfirmedUserIdsAsync(CancellationToken cancellationToken = default);
}
