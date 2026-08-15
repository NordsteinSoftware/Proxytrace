using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Proxytrace.Application.Pricing.Internal;

/// <summary>
/// Periodically re-resolves model prices for every provider so cost figures track upstream
/// pricing changes. Mirrors the cleanup background services.
/// </summary>
internal sealed class PriceRefreshService : BackgroundService
{
    private readonly PriceRefreshConfiguration configuration;
    private readonly IModelPriceRefresher refresher;
    private readonly ILogger<PriceRefreshService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PriceRefreshService"/> class.
    /// </summary>
    public PriceRefreshService(
        PriceRefreshConfiguration configuration,
        IModelPriceRefresher refresher,
        ILogger<PriceRefreshService> logger)
    {
        this.configuration = configuration;
        this.refresher = refresher;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var period = TimeSpan.FromHours(Math.Max(1, configuration.RefreshIntervalHours));
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

            try
            {
                await refresher.RefreshAllAsync(cancellationToken);
                logger.LogInformation("Refreshed model prices for all providers");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Periodic model price refresh failed");
            }
        }
    }
}
