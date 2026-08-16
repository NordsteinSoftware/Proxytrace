using Proxytrace.Domain.Demo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Proxytrace.Application.Demo.Internal;

internal sealed class DemoSeederHostedService : IHostedService
{
    private readonly IServiceProvider rootServices;
    private readonly ILogger<DemoSeederHostedService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DemoSeederHostedService"/> class.
    /// </summary>
    public DemoSeederHostedService(
        IServiceProvider rootServices,
        ILogger<DemoSeederHostedService> logger)
    {
        this.rootServices = rootServices;
        this.logger = logger;
    }

    /// <summary>
    /// Starts asynchronously.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = rootServices.CreateScope();

        var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
        await initializer.EnsureDatabaseReadyAsync(cancellationToken);

        var scenarios = scope.ServiceProvider.GetServices<IDemoScenario>()
            .OrderBy(s => s.Order)
            .ToList();

        logger.LogInformation("Kiosk demo seeding: {Count} scenarios", scenarios.Count);

        foreach (var scenario in scenarios)
        {
            try
            {
                logger.LogInformation("Seeding scenario {Name} (order {Order})",
                    scenario.GetType().Name, scenario.Order);
                await scenario.SeedAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Demo scenario {Name} failed", scenario.GetType().Name);
                throw;
            }
        }

        logger.LogInformation("Kiosk demo seeding complete");
    }

    /// <summary>
    /// Stops asynchronously.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
