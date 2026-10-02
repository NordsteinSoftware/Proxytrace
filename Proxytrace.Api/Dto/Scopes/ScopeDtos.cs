using System.ComponentModel.DataAnnotations;
using Proxytrace.Domain.Scope;

namespace Proxytrace.Api.Dto.Scopes;

/// <summary>
/// A scope in the project's scope list — its identity, user-curated label and within-retention
/// activity, plus the ids of its member agents (so pickers and the agents page can group without a
/// second round trip).
/// </summary>
public record ScopeListItemDto(
    Guid Id,
    Guid ProjectId,
    string Key,
    string? DisplayName,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastActivityAt,
    int TraceCount,
    long TotalTokens,
    IReadOnlyList<Guid> AgentIds)
{
    /// <summary>
    /// From.
    /// </summary>
    public static ScopeListItemDto From(ScopeOverview overview)
        => new(
            overview.Scope.Id,
            overview.Scope.ProjectId,
            overview.Scope.ExternalKey,
            overview.Scope.DisplayName,
            overview.Scope.Description,
            overview.Scope.CreatedAt,
            overview.LastActivityAt,
            overview.TraceCount,
            overview.TotalTokens,
            overview.Agents.Select(a => a.AgentId).ToArray());
}

/// <summary>
/// One member agent of a scope with its activity inside that scope.
/// </summary>
public record ScopeAgentDto(
    Guid AgentId,
    string AgentName,
    int TraceCount,
    long TotalTokens,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

/// <summary>
/// A scope with its per-agent activity.
/// </summary>
public record ScopeDetailDto(
    Guid Id,
    Guid ProjectId,
    string Key,
    string? DisplayName,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastActivityAt,
    int TraceCount,
    long TotalTokens,
    IReadOnlyList<ScopeAgentDto> Agents);

/// <summary>
/// Request payload for editing a scope's user-curated fields. Blank values clear the field; the
/// key is immutable (it is the URL segment clients send).
/// </summary>
public record UpdateScopeRequest(
    [StringLength(IScope.MaxDisplayNameLength)] string? DisplayName,
    [StringLength(IScope.MaxDescriptionLength)] string? Description);
