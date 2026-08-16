namespace Proxytrace.Domain.Invite;

/// <summary>
/// Repository for persisting and querying invite entities.
/// </summary>
public interface IInviteRepository : IRepository<IInvite>
{
    Task<IInvite?> FindByTokenAsync(string token, CancellationToken cancellationToken = default);
}
