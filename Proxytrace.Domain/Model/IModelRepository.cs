namespace Proxytrace.Domain.Model;

/// <summary>
/// Repository for persisting and querying model entities.
/// </summary>
public interface IModelRepository : IRepository<IModel>
{
    Task<IModel> GetOrCreateAsync(string name, CancellationToken cancellationToken = default);
}
