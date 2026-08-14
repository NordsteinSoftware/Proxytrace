using Proxytrace.Domain.Model;
using Proxytrace.Domain.ModelProvider;
using Nordstein.Core.AI.Completions;

namespace Proxytrace.Domain.ModelEndpoint;

/// <summary>
/// A model endpoint (<see cref="IModel"/> + <see cref="IModelProvider"/>)
/// </summary>
public interface IModelEndpoint : IDomainEntity, IArchivable
{
    /// <summary>
    /// The language model
    /// </summary>
    IModel Model { get; }
    
    /// <summary>
    /// The provider that serves the model (e.g. <c>OpenAI</c>).
    /// </summary>
    IModelProvider Provider { get; }
    
    /// <summary>
    /// Price of 1M input tokens (EUR), or <c>null</c> if not configured.
    /// </summary>
    decimal? InputTokenCost { get; }

    /// <summary>
    /// Price of 1M output tokens (EUR), or <c>null</c> if not configured.
    /// </summary>
    decimal? OutputTokenCost { get; }

    /// <summary>
    /// Price of 1M cached input tokens (EUR), or <c>null</c> if not configured. Cached input is the
    /// cheaper, provider-cache-served subset of the input tokens; when null the cached portion is
    /// priced at <see cref="InputTokenCost"/>.
    /// </summary>
    decimal? CachedInputTokenCost { get; }

    /// <summary>Factory delegate for creating a new model endpoint.</summary>
    public delegate IModelEndpoint CreateNew(
        IModel model,
        IModelProvider provider,
        decimal? inputTokenCost,
        decimal? outputTokenCost,
        decimal? cachedInputTokenCost);

    /// <summary>Factory delegate for reconstituting an existing model endpoint from persistence.</summary>
    public delegate IModelEndpoint CreateExisting(
        IModel model,
        IModelProvider provider,
        decimal? inputTokenCost,
        decimal? outputTokenCost,
        decimal? cachedInputTokenCost,
        IDomainEntityData existing);

    /// <summary>
    /// Calculates cost associated with this endpoint for a given usage
    /// </summary>
    decimal? CalculateCost(TokenUsage usage);
}
