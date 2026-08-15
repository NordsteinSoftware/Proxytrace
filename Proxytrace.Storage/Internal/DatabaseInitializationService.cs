using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain.Demo;

namespace Proxytrace.Storage.Internal;

/// <summary>
/// Service that ensures the database is created and migrated on application startup
/// </summary>
internal class DatabaseInitializationService : IHostedService, IDatabaseInitializer
{
    private readonly IServiceProvider serviceProvider;
    private readonly StorageConfiguration configuration;
    private readonly ILogger<DatabaseInitializationService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseInitializationService"/> class.
    /// </summary>
    public DatabaseInitializationService(
        IServiceProvider serviceProvider,
        StorageConfiguration configuration,
        ILogger<DatabaseInitializationService> logger)
    {
        this.serviceProvider = serviceProvider;
        this.configuration = configuration;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task EnsureDatabaseReadyAsync(CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StorageDbContext>();

        if (!configuration.SupportsMigrations)
        {
            logger.LogInformation(
                "Storage provider does not support migrations. Using EnsureCreatedAsync.");
            await context.Database.EnsureCreatedAsync(cancellationToken);
            logger.LogInformation("Database initialization completed successfully");
            return;
        }

        logger.LogInformation("Ensuring database is created and up to date via migrations...");
        await context.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("Database initialization completed successfully");
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.SupportsMigrations)
        {
            return;
        }

        try
        {
            await EnsureDatabaseReadyAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize database");
            throw;
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
