namespace Proxytrace.Storage.Internal.Entities.Inference;

/// <summary>
/// Storage value object for serializing <see cref="Nordstein.Core.AI.Completions.IModelParameters"/> as JSON.
/// </summary>
internal record ModelParametersData(
    double? Temperature,
    double? TopP,
    string? ReasoningEffort,
    double? FrequencyPenalty,
    double? PresencePenalty,
    int? MaxTokens,
    long? Seed,
    IReadOnlyList<string>? Stop,
    int? N)
{
    /// <summary>
    /// Gets the empty.
    /// </summary>
    public static ModelParametersData Empty { get; } = new(null, null, null, null, null, null, null, null, null);
}
