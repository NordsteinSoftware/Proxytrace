
namespace Proxytrace.Domain.ModelProvider;

/// <summary>
/// A model provider (e.g. OpenAI) with its API endpoint and credentials. Archivable: deleting a
/// provider soft-archives it (and its endpoints) instead of hard-deleting, so the AgentCall/TestRun
/// history that references its endpoints by id is preserved rather than cascade-removed.
/// </summary>
public interface IModelProvider : IDomainEntity, IArchivable
{
    /// <summary>
    /// The name of the model provider (e.g. OpenAI)
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The endpoint URL for the model provider's API (e.g. https://api.openai.com/v1)
    /// </summary>
    Uri Endpoint { get; }

    /// <summary>
    /// The API key for authenticating at the model provider (this is not the Proxytrace Api Key)
    /// </summary>
    string ApiKey { get; }

    /// <summary>
    /// The kind of model provider (determines which SDK/protocol is used for AI calls)
    /// </summary>
    ModelProviderKind Kind { get; }

    /// <summary>Factory delegate for creating a new model provider.</summary>
    public delegate IModelProvider CreateNew(
        string name,
        Uri endpoint,
        string apiKey,
        ModelProviderKind kind);

    /// <summary>Factory delegate for reconstituting an existing model provider from persistence.</summary>
    public delegate IModelProvider CreateExisting(
        string name,
        Uri endpoint,
        string apiKey,
        ModelProviderKind kind,
        IDomainEntityData existing);

    IProviderClient CreateClient();
}
