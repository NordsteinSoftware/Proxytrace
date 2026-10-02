using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nordstein.Core.Common.Time;
using Proxytrace.Domain.Scope;

namespace Proxytrace.Application.Ingestion.Internal;

/// <summary>
/// Admits the scope key of an ingested call (<see cref="IScopeRepository.AdmitAsync"/>), and
/// remembers for <see cref="FullProjectMemo"/> which projects are at their scope cap. A client that
/// keeps sending fresh per-user keys after the cap is hit would otherwise pay the admission COUNT on
/// every call and log a warning per call; while a project is remembered as full, an unseen key costs
/// the same single key probe as a known one and is ingested unscoped silently, with one warning per
/// window.
/// </summary>
/// <remarks>
/// Remembering "full" is safe in a way caching ids is not (see <c>ScopeRepository.AdmitAsync</c>):
/// a stale entry can only leave a new scope unscoped for up to one window after a slot frees up —
/// known scopes are still found by key — and never stamps a trace with an id that has no row.
/// Singleton, per process; each ingesting replica keeps its own memo.
/// </remarks>
internal sealed class ScopeAdmission
{
    /// <summary>How long a project that hit its scope cap is remembered as full.</summary>
    internal static readonly TimeSpan FullProjectMemo = TimeSpan.FromMinutes(1);

    private readonly IScopeRepository scopes;
    private readonly ScopeOptions options;
    private readonly IClock clock;
    private readonly ILogger<ScopeAdmission> logger;
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> fullUntil = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ScopeAdmission"/> class.
    /// </summary>
    public ScopeAdmission(IScopeRepository scopes, ScopeOptions options, IClock clock, ILogger<ScopeAdmission> logger)
    {
        this.scopes = scopes;
        this.options = options;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>
    /// The id of the project's scope <paramref name="canonicalKey"/>, creating it on first sight, or
    /// <see langword="null"/> when it is new and the project is at its cap.
    /// </summary>
    public async Task<Guid?> AdmitAsync(Guid projectId, string canonicalKey, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (fullUntil.TryGetValue(projectId, out var until) && until > now)
            return await scopes.FindIdByKeyAsync(projectId, canonicalKey, cancellationToken);

        var scopeId = await scopes.AdmitAsync(projectId, canonicalKey, options.MaxScopesPerProject, cancellationToken);
        if (scopeId is not null)
        {
            // An expired entry whose project has room again.
            fullUntil.TryRemove(projectId, out _);
            return scopeId;
        }

        fullUntil[projectId] = now + FullProjectMemo;
        logger.LogWarning(
            "Project {ProjectId} reached its limit of {MaxScopes} scopes; ingesting calls with unseen scope keys (e.g. {ScopeKey}) unscoped for the next {MemoSeconds}s",
            projectId, options.MaxScopesPerProject, canonicalKey, FullProjectMemo.TotalSeconds);
        return null;
    }
}
