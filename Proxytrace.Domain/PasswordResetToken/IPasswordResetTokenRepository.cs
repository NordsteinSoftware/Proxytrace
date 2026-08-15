namespace Proxytrace.Domain.PasswordResetToken;

/// <summary>
/// Repository for persisting and querying password reset token entities.
/// </summary>
public interface IPasswordResetTokenRepository : IRepository<IPasswordResetToken>
{
    Task<IPasswordResetToken?> FindByTokenAsync(string token, CancellationToken cancellationToken = default);
}
