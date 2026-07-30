namespace Proxytrace.Application.Ingestion;

/// <summary>
/// Tracks whether the current calendar month's trace ingestion has exceeded the licensed quota.
/// Ingestion consults this to drop traces once the cap is reached.
/// </summary>
public interface ITraceQuotaGuard
{
    /// <summary>
    /// True when the installation as a whole has reached its licensed monthly trace limit.
    /// </summary>
    /// <remarks>
    /// This is the <b>installation</b> view, and it is not the right question to ask before dropping
    /// a specific trace — use <see cref="IsOverQuota"/> for that. Being at the cap install-wide does
    /// not mean every project should stop capturing; see the fair-share rule there.
    /// </remarks>
    bool IsCurrentMonthOverQuota { get; }

    /// <summary>
    /// True when a trace belonging to <paramref name="projectId"/> should be dropped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The licensed limit is installation-wide, but enforcing it as a single global switch meant one
    /// busy project could consume the whole month's allowance and silently stop capture for every
    /// other project — including projects that had ingested almost nothing.
    /// </para>
    /// <para>
    /// The limit therefore stays global while the <i>drop decision</i> is per project: once the
    /// installation is at its cap, a project is dropped only while it sits above its equal share of
    /// that cap. Since the total is at or over the cap, at least one project must be above its
    /// share, so the licensed limit still binds — but the projects consuming it are the ones
    /// throttled, and a quiet project keeps capturing.
    /// </para>
    /// </remarks>
    bool IsOverQuota(Guid projectId);
}
