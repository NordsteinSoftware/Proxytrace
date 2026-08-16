using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain.ApplicationError;

namespace Proxytrace.Application.ErrorLog.Internal;

/// <summary>
/// Periodically prunes the captured-error table: age-based rotation plus a hard count cap to bound
/// growth during an error storm. Mirrors <c>AgentCallCleanupService</c>.
/// </summary>
internal sealed class ErrorLogCleanupService : BackgroundService
{
    private readonly ErrorLogCleanupConfiguration configuration;
    private readonly ILogger<ErrorLogCleanupService> logger;
    private readonly IApplicationErrorRepository repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorLogCleanupService"/> class.
    /// </summary>
    public ErrorLogCleanupService(
        ErrorLogCleanupConfiguration configuration,
        ILogger<ErrorLogCleanupService> logger,
        IApplicationErrorRepository repository)
    {
        this.configuration = configuration;
        this.logger = logger;
        this.repository = repository;

        if (configuration.RetentionDurationDays <= 0)
        {
            throw new ArgumentException("RetentionDurationDays must be greater than zero");
        }

        if (configuration.MaxRetained <= 0)
        {
            throw new ArgumentException("MaxRetained must be greater than zero");
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
            var removedByAge = await repository.RemoveOlderThanAsync(cutoff, cancellationToken);
            var removedByCap = await repository.TrimToNewestAsync(configuration.MaxRetained, cancellationToken);

            logger.LogInformation(
                "Removed {RemovedByAge} application errors older than {RetentionDays} days and {RemovedByCap} over the {MaxRetained} cap",
                removedByAge, configuration.RetentionDurationDays, removedByCap, configuration.MaxRetained);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Application error cleanup failed");
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
