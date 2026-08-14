using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nordstein.Core.Common.DependencyInjection;
using Proxytrace.Domain.Demo;
using Proxytrace.Domain.Outliers;
using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.TestSupport;
using Proxytrace.Storage.Internal;
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

        // The product-agnostic storage foundation (Nordstein.Core.Storage): the ambient-transaction
        // seam, the generic repositories/caches, and assembly-scoped discovery of this project's
        // stored entities, EF configurations and repositories. Storage-only join entities that carry
        // no Id — and so do not implement IEntity — are listed explicitly, because the IEntity scan
        // that finds the rest would miss them.
        //
        // AgentCallToolEntity is deliberately NOT listed: unlike the bare join records above it
        // extends Entity and therefore already implements IEntity, so the assembly scan registers it
        // (and its config) once. Listing it here too would double-register its IModelConfiguration.
        builder.RegisterModule(new StorageFoundationModule<StorageDbContext>(
            typeof(Module).Assembly,
            typeof(TestSuiteEvaluatorEntity),
            typeof(TestRunScheduleEndpointEntity),
            typeof(CustomAnomalyDetectorAgentEntity),
            typeof(ProjectUserEntity),
            typeof(Internal.Entities.TestResult.EvaluationStatEntity)));

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

        // Ambient-aware factory of the *concrete* context for product services (the statistics
        // queries and settings stores) that need StorageDbContext-typed access. The foundation
        // module registers the Func<DbContext> the generic repositories use; this mirrors it for the
        // concrete type. The cast is safe: StorageDbContext is the only context type ever created, so
        // an active ambient context is always a StorageDbContext.
        builder.Register<Func<StorageDbContext>>(ct =>
        {
            var scope = ct.Resolve<ILifetimeScope>();
            return () =>
            {
                var ambient = scope.Resolve<AmbientDbContext>();
                return ambient.Context as StorageDbContext ?? scope.Resolve<StorageDbContext>();
            };
        }).InstancePerLifetimeScope();

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
