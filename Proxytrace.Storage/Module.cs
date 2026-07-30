using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proxytrace.Common.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.Demo;
using Proxytrace.Domain.Outliers;
using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.TestSupport;
using Proxytrace.Storage.Internal;
using Proxytrace.Storage.Internal.Entities;
using Proxytrace.Storage.Internal.Entities.CustomAnomalyDetector;
using Proxytrace.Storage.Internal.Entities.Project;
using Proxytrace.Storage.Internal.Entities.TestRunSchedule;
using Proxytrace.Storage.Internal.Entities.TestSuite;
using Proxytrace.Storage.Internal.Statistics;

namespace Proxytrace.Storage;

/// <summary>
/// Dependency injection module
/// </summary>
public sealed class Module : Autofac.Module
{
    private readonly Func<IServiceProvider, StorageConfiguration> configurationFactory;
    private readonly bool registerApplicationServices;

    public Module(
        Func<IServiceProvider, StorageConfiguration> configurationFactory,
        bool registerApplicationServices = true)
    {
        this.configurationFactory = configurationFactory;
        this.registerApplicationServices = registerApplicationServices;
    }

    /// <summary>
    /// Add the services for storage
    /// </summary>
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder.RegisterModule<Domain.Module>();

        if (registerApplicationServices)
        {
            // Register DB initializer FIRST so its IHostedService starts before any other
            // hosted service that may query the database on startup (e.g. StatisticsBackfillHostedService).
            builder.RegisterServiceCollection(services =>
            {
                services.AddHostedService<DatabaseInitializationService>();
            });

            builder.RegisterType<DatabaseInitializationService>()
                .As<IDatabaseInitializer>()
                .SingleInstance();

            // NOTE (#270): Storage no longer references Application, so it can no longer register
            // Application.Module here. The composition roots that need the Application graph — the API
            // host plus the Storage.Tests / Domain.Tests / Application.Tests / perf harnesses — now
            // register Application.Module (and Infrastructure's SecretProtectionModule for the at-rest
            // secret seams) themselves. This flag still gates Storage's own startup/init hosted services
            // (the DB initializer above + the backfills below); the lean proxy passes false to attach
            // read-only with no schema init or backfills.

            // One-time, idempotent backfill that protects pre-retrofit plaintext secrets. Registered
            // after the DB initializer so it runs once migrations have applied. Resolvable as itself so
            // tests can drive it directly.
            builder.RegisterType<SecretsBackfillService>()
                .AsSelf()
                .SingleInstance();
            builder.RegisterServiceCollection(services =>
                services.AddHostedService(sp => sp.GetRequiredService<SecretsBackfillService>()));

            // One-time, idempotent backfill of the denormalised trace message preview for rows ingested
            // before that column existed. Registered after the DB initializer so it runs once migrations
            // have applied. Resolvable as itself so tests can drive it directly.
            builder.RegisterType<AgentCallPreviewBackfillService>()
                .AsSelf()
                .SingleInstance();
            builder.RegisterServiceCollection(services =>
                services.AddHostedService(sp => sp.GetRequiredService<AgentCallPreviewBackfillService>()));

            // One-time, idempotent backfill of the per-call tool-name rows for traces ingested before
            // that table existed. Registered after the DB initializer so it runs once migrations
            // have applied. Resolvable as itself so tests can drive it directly.
            builder.RegisterType<AgentCallToolBackfillService>()
                .AsSelf()
                .SingleInstance();
            builder.RegisterServiceCollection(services =>
                services.AddHostedService(sp => sp.GetRequiredService<AgentCallToolBackfillService>()));

            // One-time, idempotent backfill of the evaluator-statistics projection for test results
            // recorded before that table existed. Registered after the DB initializer so it runs once
            // migrations have applied. Resolvable as itself so tests can drive it directly.
            builder.RegisterType<EvaluationStatBackfillService>()
                .AsSelf()
                .SingleInstance();
            builder.RegisterServiceCollection(services =>
                services.AddHostedService(sp => sp.GetRequiredService<EvaluationStatBackfillService>()));
        }

        builder.Register<StorageConfiguration>(ct => configurationFactory(ct.Resolve<IServiceProvider>())).SingleInstance();

        builder.Register<DbContextOptions<StorageDbContext>>(ct =>
        {
            var dbBuilder = new DbContextOptionsBuilder<StorageDbContext>();
            ConfigureStorage(dbBuilder, ct.Resolve<StorageConfiguration>());
            return dbBuilder.Options;
        }).SingleInstance();

        builder.RegisterType<StorageDbContext>()
            .AsSelf()
            .InstancePerDependency();

        // Ambient-aware context factory: while a logical transaction is active, every repository,
        // mapper and query resolves the single shared transactional context (read-your-writes on
        // one connection). Outside a transaction it hands out a fresh context per call. This
        // explicit Func<StorageDbContext> registration overrides Autofac's auto-generated one.
        //
        // Note the fresh-resolve branch tracks the context on the *resolving* scope until that scope
        // disposes — fine on a short-lived request scope, but a singleton resolved from the root
        // container would accumulate one per call until process shutdown. A non-transactional
        // batch/read loop in a singleton hosted service must therefore take a disposable context via
        // Autofac's auto-provided Func<Owned<StorageDbContext>> and dispose it per batch instead (see
        // AgentCallPreviewBackfillService / SecretsBackfillService, issue #256).
        builder.Register<Func<StorageDbContext>>(ct =>
        {
            var scope = ct.Resolve<ILifetimeScope>();
            return () =>
            {
                var ambient = scope.Resolve<AmbientDbContext>();
                return ambient.Context ?? scope.Resolve<StorageDbContext>();
            };
        }).InstancePerLifetimeScope();

        ConfigureEntities(builder);
        ConfigureEntity(typeof(TestSuiteEvaluatorEntity), builder);
        ConfigureEntity(typeof(TestRunScheduleEndpointEntity), builder);
        ConfigureEntity(typeof(CustomAnomalyDetectorAgentEntity), builder);
        ConfigureEntity(typeof(ProjectUserEntity), builder);
        ConfigureEntity(typeof(Internal.Entities.TestResult.EvaluationStatEntity), builder);
        // AgentCallToolEntity is NOT listed here like the storage-only entities above: unlike those
        // (bare records with no Id, so ConfigureEntities' IEntity scan skips them), it extends Entity
        // and therefore already implements IEntity — ConfigureEntities already registers it and its
        // config via the assembly scan. Adding it here too would double-register its
        // IModelConfiguration, running AgentCallToolConfig.Configure twice against the same model.

        builder.RegisterType<AmbientDbContext>()
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<Transaction>()
            .As<ITransaction>();

        builder.RegisterType<TestDataReset>()
            .As<ITestDataReset>()
            .InstancePerDependency();

        builder.RegisterType<TestRunStatsStore>()
            .AsImplementedInterfaces()
            .InstancePerDependency();

        builder.RegisterType<Internal.Entities.Licensing.StoredLicenseStore>()
            .AsImplementedInterfaces()
            .InstancePerDependency();

        builder.RegisterType<Internal.Entities.EmailSettings.EmailSettingsStore>()
            .AsImplementedInterfaces()
            .InstancePerDependency();

        builder.RegisterType<Internal.Entities.OutlierSettings.OutlierSettingsStore>()
            .AsImplementedInterfaces()
            .InstancePerDependency();

        builder.RegisterType<AgentCallStatsQueries>()
            .As<IAgentCallStatsReader>()
            .InstancePerDependency();

        builder.RegisterType<OutlierBaselineQueries>()
            .As<IOutlierBaselineReader>()
            .InstancePerDependency();

        builder.RegisterType<EvaluatorStatsQueries>()
            .As<IEvaluatorStatsReader>()
            .InstancePerDependency();

        builder
            .Register(context => new AutofacServiceProvider(context.Resolve<ILifetimeScope>()))
            .InstancePerLifetimeScope()
            .IfNotRegistered(typeof(IServiceProvider));
    }

    private static void ConfigureEntities(ContainerBuilder builder)
    {
        var entityTypes = typeof(Module).Assembly
            .GetTypes()
            .Where(t => typeof(IEntity).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false })
            .ToList();

        foreach (Type entityType in entityTypes)
        {
            ConfigureEntity(entityType, builder);
        }
    }

    private static void ConfigureEntity(Type storedEntityType, ContainerBuilder builder)
    {
        builder.RegisterType(storedEntityType)
            .AsSelf();

        var configurationBaseType = typeof(AbstractEntityConfiguration<>).MakeGenericType(storedEntityType);

        // find the type that derives from configurationBaseType
        Type configurationType = typeof(Module).Assembly
                                     .GetTypes()
                                     .SingleOrDefault(t => t.IsSubclassOf(configurationBaseType))
                                 ?? throw new InvalidOperationException(
                                     $"No configuration type found for entity type {storedEntityType.Name}");

        builder
            .RegisterType(configurationType)
            .AsImplementedInterfaces()
            .InstancePerLifetimeScope();

        // get the StoredDomainEntity attribute to locate the associated Domain Entity Type
        var domainEntityType = storedEntityType.GetDomainEntityType();
        if (domainEntityType != null)
        {
            var repositoryBaseType = typeof(AbstractRepository<,>).MakeGenericType(domainEntityType, storedEntityType);
            // find the type that derives from repositoryBaseType
            Type repositoryType = typeof(Module).Assembly
                                      .GetTypes()
                                      .SingleOrDefault(t => t.IsSubclassOf(repositoryBaseType))
                                  ?? throw new InvalidOperationException(
                                      $"No repository type found for entity type {storedEntityType.Name}");

            // register repository type as all registered interfaces
            foreach (Type interfaceType in repositoryType.GetInterfaces())
            {
                builder.RegisterType(repositoryType).As(interfaceType);
            }

            // opt-in in-memory cache for slow-changing reference data
            if (storedEntityType.GetCustomAttribute<CacheableAttribute>() != null)
            {
                // The invalidation registry is a singleton; the cache that consults it is NOT.
                // Cached domain entities hold the repository they were materialized from, which
                // closes over its resolving lifetime scope — caching them in the root container
                // would hand out entities bound to a disposed request scope (fc4b5f72). Keeping
                // the entries scope-local and the *versions* process-wide gives cross-scope
                // write-through invalidation without reintroducing that. See EntityCacheVersions.
                Type versionsType = typeof(EntityCacheVersions<>).MakeGenericType(domainEntityType);
                builder.RegisterType(versionsType).AsSelf().SingleInstance();

                Type cacheImpl = typeof(EntityCache<>).MakeGenericType(domainEntityType);
                Type cacheInterface = typeof(IEntityCache<>).MakeGenericType(domainEntityType);
                builder.RegisterType(cacheImpl).As(cacheInterface).InstancePerLifetimeScope();
            }
        }
    }

    private static void ConfigureStorage(DbContextOptionsBuilder options, StorageConfiguration configuration)
    {
        options
            .ConfigureWarnings(b =>
            {
                b.Log(
                    (RelationalEventId.ConnectionOpened, LogLevel.Debug),
                    (RelationalEventId.CommandExecuted, LogLevel.Debug),
                    (RelationalEventId.ConnectionClosed, LogLevel.Debug));

                // Suppress the pending-model-changes warning across all providers.
                //
                // EF raises this at Migrate() time and treats it as an error, so an un-migrated
                // model change does not merely warn — it stops the application from starting, at a
                // customer's deployment, where nobody can fix it. The snapshot generated by the
                // EF Core 10 tools can also diverge from the runtime model in metadata-only ways
                // (explicit Schema=null annotations, Npgsql column type conventions) that are not
                // schema differences at all, which would turn a cosmetic mismatch into an outage.
                //
                // Suppressing it here does NOT mean drift goes undetected: MigrationDriftTests
                // asserts HasPendingModelChanges() is false against the relational model, which is
                // the same comparison Migrate() performs. Drift therefore fails CI — in front of
                // the people who can add the migration — rather than a production boot. Do not
                // remove this suppression without keeping that test.
                b.Ignore(RelationalEventId.PendingModelChangesWarning);

                // The in-memory provider has no real transactions; silence the warning so the
                // single EF transaction path (used by ITransaction) is a no-op under unit tests.
                if (configuration is InMemoryConfiguration)
                    b.Ignore(InMemoryEventId.TransactionIgnoredWarning);
            });

        switch (configuration)
        {
            case PostgresConfiguration postgres:
                options.UseNpgsql(postgres.ConnectionString,
                    npgsqlOptions =>
                        npgsqlOptions.MigrationsAssembly(typeof(StorageDbContext).Assembly.GetName().Name));
                break;
            case InMemoryConfiguration inMemory:
                options.UseInMemoryDatabase(Guid.NewGuid() + inMemory.Name);
                break;
            default:
                throw new NotSupportedException(
                    $"Storage configuration of type {configuration.GetType().Name} is not supported");
        }
    }
}