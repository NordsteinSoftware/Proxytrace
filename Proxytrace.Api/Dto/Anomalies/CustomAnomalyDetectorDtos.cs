using System.ComponentModel.DataAnnotations;
using Proxytrace.Domain.CustomAnomaly;

namespace Proxytrace.Api.Dto.Anomalies;

/// <summary>
/// Data transfer object representing a anomaly trigger.
/// </summary>
public record AnomalyTriggerDto(TriggerKind Kind, string Pattern);

/// <summary>
/// Data transfer object representing a custom anomaly detector.
/// </summary>
public record CustomAnomalyDetectorDto(
    Guid Id,
    string Name,
    string Instructions,
    Guid ProjectId,
    Guid EndpointId,
    string EndpointName,
    IReadOnlyList<AnomalyTriggerDto> Triggers,
    bool AllAgents,
    IReadOnlyList<Guid> AgentIds,
    bool IsEnabled,
    bool BlockUpstream,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Request payload for create custom anomaly detector operations.
/// </summary>
public sealed record CreateCustomAnomalyDetectorRequest
{
    /// <summary>
    /// Gets or sets the project id.
    /// </summary>
    public required Guid ProjectId { get; init; }
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>The LLM review instructions — become the hidden system agent's system prompt.</summary>
    public required string Instructions { get; init; }

    /// <summary>The model endpoint the hidden system agent reviews with.</summary>
    public required Guid EndpointId { get; init; }

    /// <summary>
    /// Gets or sets the triggers.
    /// </summary>
    public required IReadOnlyList<AnomalyTriggerDto> Triggers { get; init; }
    /// <summary>
    /// Gets or sets the all agents.
    /// </summary>
    public bool AllAgents { get; init; } = true;
    /// <summary>
    /// Gets or sets the agent ids.
    /// </summary>
    [MaxLength(RequestLimits.MaxScopedAgents)]
    public IReadOnlyList<Guid>? AgentIds { get; init; }
    /// <summary>
    /// Gets or sets the is enabled.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Whether the proxy rejects trigger-matching requests before they reach the provider.</summary>
    public bool BlockUpstream { get; init; }
}

/// <summary>
/// Request payload for update custom anomaly detector operations.
/// </summary>
public sealed record UpdateCustomAnomalyDetectorRequest
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Gets or sets the instructions.
    /// </summary>
    public required string Instructions { get; init; }

    /// <summary>Null keeps the hidden agent's current endpoint.</summary>
    public Guid? EndpointId { get; init; }

    /// <summary>
    /// Gets or sets the triggers.
    /// </summary>
    public required IReadOnlyList<AnomalyTriggerDto> Triggers { get; init; }
    /// <summary>
    /// Gets or sets the all agents.
    /// </summary>
    public required bool AllAgents { get; init; }
    /// <summary>
    /// Gets or sets the agent ids.
    /// </summary>
    [MaxLength(RequestLimits.MaxScopedAgents)]
    public IReadOnlyList<Guid>? AgentIds { get; init; }
    /// <summary>
    /// Gets or sets the is enabled.
    /// </summary>
    public required bool IsEnabled { get; init; }

    /// <summary>Whether the proxy rejects trigger-matching requests before they reach the provider.</summary>
    public required bool BlockUpstream { get; init; }
}
