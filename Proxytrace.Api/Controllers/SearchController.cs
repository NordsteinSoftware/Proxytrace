using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Configuration;
using Proxytrace.Api.Dto.Search;
using Proxytrace.Domain.ProjectSearchSettings;
using Proxytrace.Domain.Search;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for search operations.
/// </summary>
[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/search")]
public class SearchController : ControllerBase
{
    private readonly ISearchService searchService;
    private readonly ISearchIndexer indexer;
    private readonly IProjectSearchSettingsResolver settingsResolver;
    private readonly ISearchIndexStatistics indexStatistics;
    private readonly IReindexStateTracker reindexTracker;
    private readonly IProjectSearchSettings.CreateNew settingsFactory;
    private readonly SearchRequestOptions options;
    private readonly IProjectAccessGuard accessGuard;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchController"/> class.
    /// </summary>
    public SearchController(
        ISearchService searchService,
        ISearchIndexer indexer,
        IProjectSearchSettingsResolver settingsResolver,
        ISearchIndexStatistics indexStatistics,
        IReindexStateTracker reindexTracker,
        IProjectSearchSettings.CreateNew settingsFactory,
        SearchRequestOptions options,
        IProjectAccessGuard accessGuard)
    {
        this.searchService = searchService;
        this.indexer = indexer;
        this.settingsResolver = settingsResolver;
        this.indexStatistics = indexStatistics;
        this.reindexTracker = reindexTracker;
        this.settingsFactory = settingsFactory;
        this.options = options;
        this.accessGuard = accessGuard;
    }

    /// <summary>
    /// Searches.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<SearchResultsDto>> Search(
        Guid projectId,
        [FromQuery] string q,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < options.MinQueryLength)
        {
            return BadRequest(new { error = $"q must be at least {options.MinQueryLength} chars" });
        }
        if (q.Length > options.MaxQueryLength)
        {
            return BadRequest(new { error = $"q must be at most {options.MaxQueryLength} chars" });
        }
        if (!await accessGuard.CanAccessProjectAsync(projectId, cancellationToken))
        {
            return NotFound();
        }

        var results = await searchService.SearchAsync(projectId, q.Trim(), cancellationToken);
        var dto = new SearchResultsDto(
            results.Hits.Select(h => new SearchHitDto(
                Kind: KindToWire(h.Kind),
                EntityId: h.EntityId,
                Title: h.Title,
                Snippet: h.Snippet,
                Score: h.Score,
                Metadata: h.Metadata)).ToList());
        return Ok(dto);
    }

    /// <summary>
    /// Recent.
    /// </summary>
    [HttpGet("recent")]
    public async Task<ActionResult<SearchResultsDto>> Recent(
        Guid projectId,
        [FromQuery] string? kinds,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0 || limit > 50)
        {
            return BadRequest(new { error = "limit must be between 1 and 50" });
        }
        if (!await accessGuard.CanAccessProjectAsync(projectId, cancellationToken))
        {
            return NotFound();
        }

        var parsedKinds = new HashSet<SearchKind>();
        foreach (var raw in (kinds ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryWireToKind(raw, out var kind))
            {
                return BadRequest(new { error = $"unknown kind '{raw}'" });
            }
            parsedKinds.Add(kind);
        }

        // No explicit kind filter means "all kinds": the recent feed should surface everything
        // recently indexed, not nothing.
        if (parsedKinds.Count == 0)
        {
            foreach (var kind in Enum.GetValues<SearchKind>())
            {
                parsedKinds.Add(kind);
            }
        }

        var results = await searchService.GetRecentAsync(projectId, parsedKinds.ToArray(), limit, cancellationToken);
        var dto = new SearchResultsDto(
            results.Hits.Select(h => new SearchHitDto(
                Kind: KindToWire(h.Kind),
                EntityId: h.EntityId,
                Title: h.Title,
                Snippet: h.Snippet,
                Score: h.Score,
                Metadata: h.Metadata)).ToList());
        return Ok(dto);
    }

    /// <summary>
    /// Reindexes.
    /// </summary>
    [HttpPost("reindex")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<object>> Reindex(Guid projectId, CancellationToken cancellationToken)
    {
        await indexer.ReindexProjectAsync(projectId, cancellationToken);
        return Ok(new { reindexed = projectId });
    }

    /// <summary>
    /// Gets the settings.
    /// </summary>
    [HttpGet("settings")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<SearchIndexingSettingsDto>> GetSettings(Guid projectId, CancellationToken cancellationToken)
    {
        var settings = await settingsResolver.GetOrDefaultsAsync(projectId, cancellationToken);
        return Ok(ToDto(settings));
    }

    /// <summary>
    /// Updates the settings.
    /// </summary>
    [HttpPut("settings")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<SearchIndexingSettingsDto>> UpdateSettings(
        Guid projectId,
        [FromBody] SearchIndexingSettingsDto settings,
        CancellationToken cancellationToken)
    {
        if (settings.IndexedKinds.Count == 0)
        {
            return BadRequest(new { error = "indexedKinds must contain at least one kind" });
        }
        if (settings.SnippetLength < options.MinSnippetLength || settings.SnippetLength > options.MaxSnippetLength)
        {
            return BadRequest(new { error = $"snippetLength must be between {options.MinSnippetLength} and {options.MaxSnippetLength}" });
        }

        var kinds = new HashSet<SearchKind>();
        foreach (var raw in settings.IndexedKinds)
        {
            if (!TryWireToKind(raw, out var kind))
            {
                return BadRequest(new { error = $"unknown kind '{raw}'" });
            }
            kinds.Add(kind);
        }

        var current = await settingsResolver.GetOrDefaultsAsync(projectId, cancellationToken);
        var draft = settingsFactory(
            project: current.Project,
            enabled: settings.Enabled,
            indexedKinds: kinds.ToArray(),
            autoReindexOnChange: settings.AutoReindexOnChange,
            snippetLength: settings.SnippetLength);

        var saved = await settingsResolver.UpsertAsync(draft, cancellationToken);
        return Ok(ToDto(saved));
    }

    /// <summary>
    /// Gets the status.
    /// </summary>
    [HttpGet("status")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<SearchIndexStatusDto>> GetStatus(Guid projectId, CancellationToken cancellationToken)
    {
        var count = await indexStatistics.CountAsync(projectId, cancellationToken);
        var lastIndexedAt = await indexStatistics.LastIndexedAtAsync(projectId, cancellationToken);
        var isReindexing = reindexTracker.IsReindexing(projectId);
        return Ok(new SearchIndexStatusDto(lastIndexedAt, count, isReindexing));
    }

    private static SearchIndexingSettingsDto ToDto(IProjectSearchSettings settings)
        => new(
            Enabled: settings.Enabled,
            IndexedKinds: settings.IndexedKinds.Select(KindToWire).ToList(),
            AutoReindexOnChange: settings.AutoReindexOnChange,
            SnippetLength: settings.SnippetLength);

    private static string KindToWire(SearchKind kind) => kind switch
    {
        SearchKind.Agent => "agent",
        SearchKind.TestSuite => "testSuite",
        SearchKind.AgentCall => "agentCall",
        SearchKind.Evaluator => "evaluator",
        SearchKind.TestCase => "testCase",
        _ => kind.ToString().ToLowerInvariant(),
    };

    private static bool TryWireToKind(string wire, out SearchKind kind)
    {
        switch (wire)
        {
            case "agent": kind = SearchKind.Agent; return true;
            case "testSuite": kind = SearchKind.TestSuite; return true;
            case "agentCall": kind = SearchKind.AgentCall; return true;
            case "evaluator": kind = SearchKind.Evaluator; return true;
            case "testCase": kind = SearchKind.TestCase; return true;
            default: kind = default; return false;
        }
    }
}
