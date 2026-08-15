using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Session;
using Proxytrace.Licensing;

namespace Proxytrace.Application.Cleanup.Internal;

internal sealed class AgentCallCleanupService : BackgroundService
{
    private readonly AgentCallCleanupConfiguration configuration;
    private readonly ILogger<AgentCallCleanupService> logger;
    private readonly IAgentCallRepository agentCallRepository;
    private readonly ISessionRepository sessionRepository;
    private readonly ILicenseService license;

    private readonly int configuredRetentionDays;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentCallCleanupService"/> class.
    /// </summary>
    public AgentCallCleanupService(
        AgentCallCleanupConfiguration configuration,
        ILogger<AgentCallCleanupService> logger,
        IAgentCallRepository agentCallRepository,
        ISessionRepository sessionRepository,
        ILicenseService license)
    {
        this.configuration = configuration;
        this.logger = logger;
        this.agentCallRepository = agentCallRepository;
        this.sessionRepository = sessionRepository;
        this.license = license;

        if (configuration.RetentionDurationDays <= 0)
        {
            throw new ArgumentException("RetentionDurationDays must be greater than zero");
        }

        configuredRetentionDays = configuration.RetentionDurationDays;
    }

    /// <summary>
    /// Cleans the once asynchronously.
    /// </summary>
    public async Task CleanOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            // The license can cap how long traces are retained; never retain longer than allowed.
            var cap = license.GetLimit(LicenseLimit.TraceRetentionDays);
            var effectiveDays = cap == long.MaxValue
                ? configuredRetentionDays
                : (int)Math.Min(configuredRetentionDays, cap);
            var retentionDuration = TimeSpan.FromDays(effectiveDays);
            var cutoffDate = DateTimeOffset.UtcNow - retentionDuration;

            // Read the per-session deltas BEFORE the delete — afterwards the rows are gone and the
            // session counters would keep claiming traces that no longer exist.
            var sessionRemovals = await agentCallRepository.GetSessionRemovalsOlderThanAsync(cutoffDate, cancellationToken);
            var numRemoved = await agentCallRepository.RemoveOlderThanAsync(cutoffDate, cancellationToken);
            await sessionRepository.RecordTraceRemovalsAsync(sessionRemovals, cancellationToken);

            // Sessions have no retention of their own: a client that mints a key per run grows the
            // table forever, including sessions whose traces are long gone. A session's last activity
            // IS its newest trace, so the same cutoff removes exactly those whose every trace has now
            // been deleted — never one that still has recent traces.
            var sessionsRemoved = await sessionRepository.RemoveOlderThanAsync(cutoffDate, cancellationToken);

            logger.LogInformation(
                "Removed {numRemoved} AgentCalls and {sessionsRemoved} Sessions older than {retentionDuration} days ({adjustedSessions} session counter(s) adjusted)",
                numRemoved, sessionsRemoved, retentionDuration.TotalDays, sessionRemovals.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Removing AgentCall failed");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var period = TimeSpan.FromHours(Math.Max(1, configuration.CleanupIntervalHours));
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(period, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
            
            await CleanOnceAsync(cancellationToken);
        }
    }
}
