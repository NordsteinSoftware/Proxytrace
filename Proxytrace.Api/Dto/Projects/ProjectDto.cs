using System.ComponentModel.DataAnnotations;

namespace Proxytrace.Api.Dto.Projects;

/// <summary>
/// Data transfer object representing a project.
/// </summary>
public record ProjectDto(
    Guid Id,
    string Name,
    Guid SystemEndpointId,
    IReadOnlyList<ProjectMemberDto> Members,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Lightweight project projection for the projects list / app-wide project selector. Replaces the
/// fat <see cref="ProjectDto"/>'s member list with a count; full members are fetched per-selection
/// via <c>GET /api/projects/{id}</c> (or <c>/members</c>).
/// </summary>
public record ProjectListItemDto(
    Guid Id,
    string Name,
    Guid SystemEndpointId,
    int MemberCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Data transfer object representing a project member.
/// </summary>
public record ProjectMemberDto(Guid Id, string Email);

/// <summary>
/// Request payload for create project operations.
/// </summary>
public record CreateProjectRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [Required] Guid SystemEndpointId,
    IReadOnlyList<Guid>? MemberIds = null);

// Membership is an access-control primitive and must NOT be mass-assignable through this generic
// "update name/endpoint" call — it changes only via the dedicated add/remove-member endpoints.
/// <summary>
/// Request payload for update project operations.
/// </summary>
public record UpdateProjectRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [Required] Guid SystemEndpointId);
