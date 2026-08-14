using Nordstein.Core.AI.Completions;

namespace Proxytrace.Api.Dto.Inference;

public record ModelParametersDto(
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
    public static ModelParametersDto FromDomain(IModelParameters p) => new(
        p.Temperature,
        p.TopP,
        p.ReasoningEffort,
        p.FrequencyPenalty,
        p.PresencePenalty,
        p.MaxTokens,
        p.Seed,
        p.Stop,
        p.N);
}
