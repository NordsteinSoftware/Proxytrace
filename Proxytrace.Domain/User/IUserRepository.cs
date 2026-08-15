namespace Proxytrace.Domain.User;

/// <summary>
/// Repository for persisting and querying user entities.
/// </summary>
public interface IUserRepository : IRepository<IUser>
{
    Task<IUser?> FindByExternalSubjectAsync(string externalSubject, CancellationToken cancellationToken = default);
    Task<IUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts users currently holding the given <paramref name="role"/>. Used to enforce the
    /// "at least one Admin must remain" invariant when demoting or removing users.
    /// </summary>
    Task<int> CountByRoleAsync(UserRole role, CancellationToken cancellationToken = default);
}
