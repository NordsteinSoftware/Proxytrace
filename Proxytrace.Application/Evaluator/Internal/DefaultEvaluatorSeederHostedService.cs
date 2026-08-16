using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain.Demo;
using Proxytrace.Domain;
using Proxytrace.Domain.Project;

namespace Proxytrace.Application.Evaluator.Internal;

/// <summary>
/// On startup, ensures every existing project has its built-in default evaluators (Exact Match +
/// agentic presets). New projects are provisioned on creation instead (see the project-creation
/// path); this backfills projects created before the defaults existed. Idempotent — safe to run on
/// every boot.
/// </summary>
internal sealed class DefaultEvaluatorSeederHostedService : IHostedService
{
    private readonly IServiceProvider rootServices;
    private readonly ILogger<DefaultEvaluatorSeederHostedService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultEvaluatorSeederHostedService"/> class.
    /// </summary>
    public DefaultEvaluatorSeederHostedService(
        IServiceProvider rootServices,
        ILogger<DefaultEvaluatorSeederHostedService> logger)
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
        var provisioner = scope.ServiceProvider.GetRequiredService<IDefaultEvaluatorProvisioner>();

        var all = await projects.GetAllAsync(cancellationToken);
        logger.LogInformation("Default evaluator seeding: ensuring defaults for {Count} project(s)", all.Count);

        foreach (var project in all)
        {
            await provisioner.EnsureDefaultEvaluatorsAsync(project, cancellationToken);
        }

        logger.LogInformation("Default evaluator seeding complete");
    }

    /// <summary>
    /// Stops asynchronously.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
