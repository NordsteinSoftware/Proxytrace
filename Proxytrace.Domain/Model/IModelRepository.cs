namespace Proxytrace.Domain.Model;

public interface IModelRepository : IRepository<IModel>
{
    Task<IModel> GetOrCreateAsync(string name, CancellationToken cancellationToken = default);
}
