using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain.AuditLog;

namespace Proxytrace.Application.AuditLog.Internal;

/// <summary>
/// Periodically prunes the audit log by age. The audit log is lossless, so there is no count cap —
/// only age-based retention with a long default. Mirrors <c>ErrorLogCleanupService</c>.
/// </summary>
internal sealed class AuditLogCleanupService : BackgroundService
{
    private readonly AuditLogCleanupConfiguration configuration;
    private readonly ILogger<AuditLogCleanupService> logger;
    private readonly IAuditLogRepository repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditLogCleanupService"/> class.
    /// </summary>
    public AuditLogCleanupService(
        AuditLogCleanupConfiguration configuration,
        ILogger<AuditLogCleanupService> logger,
        IAuditLogRepository repository)
    {
        this.configuration = configuration;
        this.logger = logger;
        this.repository = repository;

        if (configuration.RetentionDurationDays <= 0)
        {
            throw new ArgumentException("RetentionDurationDays must be greater than zero");
        }
    }

    /// <summary>
    /// Cleans the once asynchronously.
    /// </summary>
    public async Task CleanOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(configuration.RetentionDurationDays);
            var removed = await repository.RemoveOlderThanAsync(cutoff, cancellationToken);

            logger.LogInformation(
                "Removed {Removed} audit entries older than {RetentionDays} days",
                removed, configuration.RetentionDurationDays);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Audit log cleanup failed");
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
