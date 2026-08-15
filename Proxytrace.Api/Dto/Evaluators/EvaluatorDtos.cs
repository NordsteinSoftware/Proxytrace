using System.Text.Json.Serialization;
using Proxytrace.Domain.Evaluator;

namespace Proxytrace.Api.Dto.Evaluators;

/// <summary>
/// Lightweight evaluator projection for pickers / select lists — id, kind, name only.
/// Avoids shipping the full <see cref="EvaluatorDetailDto"/> (system message, JSON schema, …)
/// when a caller only needs to render and choose an evaluator.
/// </summary>
public record EvaluatorListItemDto(Guid Id, EvaluatorKind Kind, string Name);

/// <summary>
/// Data transfer object representing a evaluator detail.
/// </summary>
public record EvaluatorDetailDto(
    Guid Id,
    EvaluatorKind Kind,
    string Name,
    string? SystemMessage,
    Guid ProjectId,
    string ProjectName,
    Guid? EndpointId,
    string? EndpointName,
    Guid? AgentId,
    string? JsonSchema,
    string? ExtractionPattern,
    decimal? Tolerance,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Request payload for create evaluator operations.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CreateAgenticEvaluatorRequest), nameof(EvaluatorKind.Agentic))]
[JsonDerivedType(typeof(CreateExactMatchEvaluatorRequest), nameof(EvaluatorKind.ExactMatch))]
[JsonDerivedType(typeof(CreateNumericMatchEvaluatorRequest), nameof(EvaluatorKind.NumericMatch))]
[JsonDerivedType(typeof(CreateJsonSchemaMatchEvaluatorRequest), nameof(EvaluatorKind.JsonSchemaMatch))]
public abstract record CreateEvaluatorRequest
{
    /// <summary>
    /// Gets or sets the project id.
    /// </summary>
    public required Guid ProjectId { get; init; }
}

/// <summary>
/// Request payload for create agentic evaluator operations.
/// </summary>
public sealed record CreateAgenticEvaluatorRequest : CreateEvaluatorRequest
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Gets or sets the system message.
    /// </summary>
    public required string SystemMessage { get; init; }
}

/// <summary>
/// Request payload for create exact match evaluator operations.
/// </summary>
public sealed record CreateExactMatchEvaluatorRequest : CreateEvaluatorRequest;

/// <summary>
/// Request payload for create numeric match evaluator operations.
/// </summary>
public sealed record CreateNumericMatchEvaluatorRequest : CreateEvaluatorRequest
{
    /// <summary>
    /// Gets or sets the extraction pattern.
    /// </summary>
    public required string ExtractionPattern { get; init; }
    /// <summary>
    /// Gets or sets the tolerance.
    /// </summary>
    public required decimal Tolerance { get; init; }
}

/// <summary>
/// Request payload for create json schema match evaluator operations.
/// </summary>
public sealed record CreateJsonSchemaMatchEvaluatorRequest : CreateEvaluatorRequest
{
    /// <summary>
    /// Gets or sets the json schema.
    /// </summary>
    public required string JsonSchema { get; init; }
}

/// <summary>
/// Request payload for update evaluator operations.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(UpdateAgenticEvaluatorRequest), nameof(EvaluatorKind.Agentic))]
[JsonDerivedType(typeof(UpdateExactMatchEvaluatorRequest), nameof(EvaluatorKind.ExactMatch))]
[JsonDerivedType(typeof(UpdateNumericMatchEvaluatorRequest), nameof(EvaluatorKind.NumericMatch))]
[JsonDerivedType(typeof(UpdateJsonSchemaMatchEvaluatorRequest), nameof(EvaluatorKind.JsonSchemaMatch))]
public abstract record UpdateEvaluatorRequest;

/// <summary>
/// Request payload for update agentic evaluator operations.
/// </summary>
public sealed record UpdateAgenticEvaluatorRequest : UpdateEvaluatorRequest
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string? Name { get; init; }
    /// <summary>
    /// Gets or sets the system message.
    /// </summary>
    public string? SystemMessage { get; init; }
}

/// <summary>
/// Request payload for update exact match evaluator operations.
/// </summary>
public sealed record UpdateExactMatchEvaluatorRequest : UpdateEvaluatorRequest;

/// <summary>
/// Request payload for update numeric match evaluator operations.
/// </summary>
public sealed record UpdateNumericMatchEvaluatorRequest : UpdateEvaluatorRequest
{
    /// <summary>
    /// Gets or sets the extraction pattern.
    /// </summary>
    public string? ExtractionPattern { get; init; }
    /// <summary>
    /// Gets or sets the tolerance.
    /// </summary>
    public decimal? Tolerance { get; init; }
}

/// <summary>
/// Request payload for update json schema match evaluator operations.
/// </summary>
public sealed record UpdateJsonSchemaMatchEvaluatorRequest : UpdateEvaluatorRequest
{
    /// <summary>
    /// Gets or sets the json schema.
    /// </summary>
    public string? JsonSchema { get; init; }
}

/// <summary>
/// Data transfer object representing a agentic evaluator preset.
/// </summary>
public record AgenticEvaluatorPresetDto(string Key, string Name, string SystemPrompt);
