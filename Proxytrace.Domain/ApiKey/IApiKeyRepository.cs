namespace Proxytrace.Domain.ApiKey;

/// <summary>
/// Repository for <see cref="IApiKey"/>
/// </summary>
public interface IApiKeyRepository : IRepository<IApiKey>
{
    /// <summary>
    /// Tries to find an API key by its key value. Returns the API key if found, or <see langword="null"/> if not found.
    /// </summary>
    Task<IApiKey?> FindByKeyAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all API keys belonging to the provider identified by <paramref name="providerId"/>.
    /// </summary>
    Task<IReadOnlyList<IApiKey>> GetByProviderAsync(Guid providerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all API keys belonging to the project identified by <paramref name="projectId"/> —
    /// the naming lookup behind the Costs page's per-key spend breakdown and the key picker in the
    /// budget editor.
    /// </summary>
    Task<IReadOnlyList<IApiKey>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
}