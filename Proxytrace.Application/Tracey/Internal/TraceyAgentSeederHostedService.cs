using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain.Demo;
using Proxytrace.Domain;
using Proxytrace.Domain.Project;

namespace Proxytrace.Application.Tracey.Internal;

/// <summary>
/// On startup, ensures every existing project has its built-in Tracey system agent. New projects are
/// provisioned on creation instead (see the project-creation path); this backfills projects created
/// before Tracey existed. Idempotent — safe to run on every boot.
/// </summary>
internal sealed class TraceyAgentSeederHostedService : IHostedService
{
    private readonly IServiceProvider rootServices;
    private readonly ILogger<TraceyAgentSeederHostedService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TraceyAgentSeederHostedService"/> class.
    /// </summary>
    public TraceyAgentSeederHostedService(
        IServiceProvider rootServices,
        ILogger<TraceyAgentSeederHostedService> logger)
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

        var projects = scope.ServiceProvider.GetRequiredService<IRepository<IProject>>();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITraceyAgentProvisioner>();

        var all = await projects.GetAllAsync(cancellationToken);
        logger.LogInformation("Tracey seeding: ensuring Tracey agent for {Count} project(s)", all.Count);

        foreach (var project in all)
        {
            await provisioner.EnsureTraceyAgentAsync(project, cancellationToken);
        }

        logger.LogInformation("Tracey seeding complete");
    }

    /// <summary>
    /// Stops asynchronously.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
