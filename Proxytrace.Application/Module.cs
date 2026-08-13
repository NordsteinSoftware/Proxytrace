using Proxytrace.Domain.AuditLog;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Application.Agent;
using Proxytrace.Application.Anomaly;
using Proxytrace.Application.Anomaly.Internal;
using Proxytrace.Application.AuditLog;
using Proxytrace.Application.AuditLog.Internal;
using Proxytrace.Application.Auth;
using Proxytrace.Application.Auth.Internal;
using Proxytrace.Application.Auth.Local;
using Proxytrace.Application.Auth.Local.Internal;
using Proxytrace.Application.Cleanup;
using Proxytrace.Application.Cleanup.Internal;
using Proxytrace.Application.CostControl;
using Proxytrace.Application.CostControl.Internal;
using Proxytrace.Application.CustomAnomaly;
using Proxytrace.Application.CustomAnomaly.Internal;
using Proxytrace.Application.Demo;
using Proxytrace.Application.Demo.Internal;
using Proxytrace.Application.ErrorLog;
using Proxytrace.Application.ErrorLog.Internal;
using Proxytrace.Application.Evaluator;
using Proxytrace.Application.Evaluator.Internal;
using Proxytrace.Application.Ingestion;
using Proxytrace.Application.Ingestion.Internal;
using Proxytrace.Application.Licensing;
using Proxytrace.Application.Licensing.Internal;
using Proxytrace.Application.Notifications;
using Proxytrace.Application.Notifications.Internal;
using Proxytrace.Application.Outliers;
using Proxytrace.Application.Playground;
using Proxytrace.Application.Pricing;
using Proxytrace.Application.Pricing.Internal;
using Proxytrace.Application.Search;
using Proxytrace.Application.Setup;
using Proxytrace.Application.Setup.Internal;
using Proxytrace.Application.Streaming;
using Proxytrace.Application.Streaming.Internal;
using Proxytrace.Application.TestRun;
using Proxytrace.Application.TestRun.Internal;
using Proxytrace.Application.Tracey;
using Proxytrace.Application.Tracey.Internal;
using Proxytrace.Application.Updates;
using Proxytrace.Application.Updates.Internal;
using Nordstein.Core.Common.DependencyInjection;
using Nordstein.Core.Common.Hosting;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Licensing;

namespace Proxytrace.Application;

public sealed class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        // Licensing. A composition root (e.g. the API) registers the Licensing module itself with
        // an environment-derived configuration before this module loads (setting the key below);
        // otherwise fall back to the Free-tier default used by tests and the in-process kiosk.
        if (!builder.Properties.ContainsKey(Proxytrace.Licensing.Module.RegisteredKey))
        {
            builder.Properties[Proxytrace.Licensing.Module.RegisteredKey] = true;
            builder.RegisterModule(new Proxytrace.Licensing.Module(new LicensingConfiguration
            {
                ServerUrl = "https://license.proxytrace.dev",
                PublicKeys = LicensePublicKeys.GetActiveKeys(),
                LicenseJwt = null,
                CacheFilePath = Path.Combine(Path.GetTempPath(), "proxytrace-license-cache.json"),
            }));
        }

        builder.RegisterType<LicenseKeyManager>()
            .As<ILicenseKeyManager>()
            .InstancePerDependency();

        // Applies the database-stored license after the database initializer has run (this
        // module loads after the storage module registers its initializer, so hosted-service
        // start order is preserved).
        builder.RegisterServiceCollection(services =>
            services.AddHostedService<StoredLicenseStartupService>());

        builder.RegisterType<AgentCallCleanupService>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterServiceCollection(services =>
            services.AddHostedService(sc => sc.GetRequiredService<AgentCallCleanupService>()));

        builder.RegisterType<ModelPriceRefresher>()
            .As<IModelPriceRefresher>()
            .SingleInstance();
        builder.RegisterType<PriceRefreshService>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterServiceCollection(services =>
            services.AddHostedService(sc => sc.GetRequiredService<PriceRefreshService>()));

        // Update check. The composition root (the API) binds the "Updates" config section;
        // this default keeps the type resolvable in tests and the in-process kiosk.
        builder.RegisterInstance(new UpdatesConfiguration())
            .IfNotRegistered(typeof(UpdatesConfiguration));
        builder.RegisterType<UpdateCheckService>()
            .As<IUpdateService>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterServiceCollection(services =>
        {
            services.AddHttpClient(UpdateCheckService.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                // GitHub's API rejects requests without a User-Agent.
                client.DefaultRequestHeaders.UserAgent.ParseAdd("proxytrace-update-check");
            });
            services.AddHostedService(sc => sc.GetRequiredService<UpdateCheckService>());
        });

        builder.RegisterType<TraceBroadcaster>()
            .As<ITraceBroadcaster>()
            .SingleInstance();

        builder.RegisterType<TestResultBroadcaster>()
            .As<ITestResultBroadcaster>()
            .SingleInstance();

        builder.RegisterType<ProposalBroadcaster>()
            .As<IProposalBroadcaster>()
            .SingleInstance();

        builder.RegisterType<TheoryBroadcaster>()
            .As<ITheoryBroadcaster>()
            .SingleInstance();

        builder.RegisterType<NotificationBroadcaster>()
            .As<INotificationBroadcaster>()
            .SingleInstance();

        builder.RegisterType<CustomAnomalyBroadcaster>()
            .As<ICustomAnomalyBroadcaster>()
            .SingleInstance();

        // Records attribution + SSE + notification for calls the proxy blocked before upstream.
        builder.RegisterType<BlockedCallRecorder>()
            .As<IBlockedCallRecorder>()
            .SingleInstance();

        // Notification system. NotificationService fans every NotificationRequest out to all
        // registered INotificationChannel implementations (auto-discovered below). v1 ships the
        // dashboard channel (persists + SSE) and an email stub; future channels need no caller change.
        builder.RegisterType<NotificationService>()
            .As<INotificationService>()
            .SingleInstance();

        builder.RegisterAssemblyTypes(typeof(Module).Assembly)
            .Where(t => typeof(INotificationChannel).IsAssignableFrom(t)
                        && t is { IsClass: true, IsAbstract: false })
            .As<INotificationChannel>()
            .SingleInstance();

        // Anomaly detection. Mirrors the optimizer: a queue fed from the test runner on group
        // completion, drained by a background worker that raises notifications for detected anomalies.
        builder.RegisterInstance(new AnomalyDetectionConfiguration())
            .IfNotRegistered(typeof(AnomalyDetectionConfiguration));

        builder.RegisterType<AnomalyDetector>()
            .As<IAnomalyDetector>()
            .SingleInstance();

        builder.RegisterType<AnomalyInputFactory>()
            .As<IAnomalyInputFactory>()
            .SingleInstance();

        builder.RegisterType<AnomalyDetectionService>()
            .As<IAnomalyDetectionService>()
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(AnomalyDetectionService));

        const string anomalyDetectionHostedServiceKey = "Proxytrace.Application.AnomalyDetectionService.Registered";
        if (!builder.Properties.ContainsKey(anomalyDetectionHostedServiceKey))
        {
            builder.Properties[anomalyDetectionHostedServiceKey] = true;
            builder.RegisterServiceCollection(services
                => services.AddSingleton<IHostedService>(sc =>
                {
                    var kiosk = sc.GetRequiredService<KioskOptions>();
                    var endpoint = sc.GetRequiredService<KioskEndpointOptions>();

                    // Disabled only in a read-only kiosk (no LLM endpoint), matching the test runner:
                    // with no runs executing there are no groups to inspect.
                    return kiosk.Enabled && !endpoint.IsConfigured
                        ? new NullHostedService()
                        : sc.GetRequiredService<AnomalyDetectionService>();
                }));
        }

        // Custom anomaly review. Mirrors the anomaly detection service above: a queue fed from the
        // ingestion processor per persisted call, drained by a background worker that trigger-gates
        // an LLM review per matching detector.
        builder.RegisterType<CustomAnomalyReviewService>()
            .As<ICustomAnomalyReviewQueue>()
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(CustomAnomalyReviewService));

        const string customAnomalyReviewHostedServiceKey = "Proxytrace.Application.CustomAnomalyReviewService.Registered";
        if (!builder.Properties.ContainsKey(customAnomalyReviewHostedServiceKey))
        {
            builder.Properties[customAnomalyReviewHostedServiceKey] = true;
            builder.RegisterServiceCollection(services
                => services.AddSingleton<IHostedService>(sc =>
                {
                    var kiosk = sc.GetRequiredService<KioskOptions>();
                    var endpoint = sc.GetRequiredService<KioskEndpointOptions>();

                    // Disabled only in a read-only kiosk (no LLM endpoint), matching the test
                    // runner: with no judge model configured, reviews cannot run.
                    return kiosk.Enabled && !endpoint.IsConfigured
                        ? new NullHostedService()
                        : sc.GetRequiredService<CustomAnomalyReviewService>();
                }));
        }

        builder.RegisterModule<Optimization.Module>();
        builder.RegisterModule<Statistics.Module>();
        builder.RegisterModule<PlaygroundModule>();
        builder.RegisterModule<TestCase.TestCaseModule>();

        builder.RegisterModule<SearchModule>();

        builder.RegisterType<AgentNameGenerator>()
            .As<IAgentNameGenerator>()
            .SingleInstance();

        builder.RegisterType<TestRunnerService>()
            .AsImplementedInterfaces()
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(TestRunnerService));

        const string testRunnerHostedServiceKey = "Proxytrace.Application.TestRunnerService.Registered";
        if (!builder.Properties.ContainsKey(testRunnerHostedServiceKey))
        {
            builder.Properties[testRunnerHostedServiceKey] = true;
            builder.RegisterServiceCollection(services
                => services.AddSingleton<IHostedService>(sc =>
                {
                    var kiosk = sc.GetRequiredService<KioskOptions>();
                    var endpoint = sc.GetRequiredService<KioskEndpointOptions>();

                    // Disabled only in a read-only kiosk (no LLM endpoint). A configured
                    // endpoint makes kiosk fully interactive, so background test runs execute.
                    return kiosk.Enabled && !endpoint.IsConfigured
                        ? new NullHostedService()
                        : sc.GetRequiredService<TestRunnerService>();
                }));
        }

        builder.RegisterInstance(new TestRunSchedulerConfiguration())
            .IfNotRegistered(typeof(TestRunSchedulerConfiguration));

        builder.RegisterType<TestRunSchedulerService>()
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(TestRunSchedulerService));

        const string testRunSchedulerHostedServiceKey = "Proxytrace.Application.TestRunSchedulerService.Registered";
        if (!builder.Properties.ContainsKey(testRunSchedulerHostedServiceKey))
        {
            builder.Properties[testRunSchedulerHostedServiceKey] = true;
            builder.RegisterServiceCollection(services
                => services.AddSingleton<IHostedService>(sc =>
                {
                    var kiosk = sc.GetRequiredService<KioskOptions>();
                    var endpoint = sc.GetRequiredService<KioskEndpointOptions>();

                    // Disabled only in a read-only kiosk (no LLM endpoint). A configured
                    // endpoint makes kiosk fully interactive, so scheduled runs execute.
                    return kiosk.Enabled && !endpoint.IsConfigured
                        ? new NullHostedService()
                        : sc.GetRequiredService<TestRunSchedulerService>();
                }));
        }

        builder.RegisterType<OpenAiCallParser>()
            .As<IOpenAiCallParser>()
            .SingleInstance();

        builder.RegisterType<AgentVersionMatcher>()
            .As<IAgentVersionMatcher>()
            .SingleInstance();

        builder.Register(_ => new AgentVersioningOptions())
            .As<AgentVersioningOptions>()
            .SingleInstance()
            .IfNotRegistered(typeof(AgentVersioningOptions));

        // Ingestion transport. A host running the proxy/app split registers the Redis-backed
        // Messaging module itself (and sets this key) before this module loads; otherwise fall
        // back to the in-process stream used by the test suite and single-process runs.
        const string messagingModuleKey = "Proxytrace.Messaging.Registered";
        if (!builder.Properties.ContainsKey(messagingModuleKey))
        {
            builder.Properties[messagingModuleKey] = true;
            builder.RegisterModule(new Messaging.Module());
        }

        builder.RegisterType<TraceQuotaGuard>()
            .As<ITraceQuotaGuard>()
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(TraceQuotaGuard));

        const string traceQuotaGuardKey = "Proxytrace.Application.TraceQuotaGuard.Registered";
        if (!builder.Properties.ContainsKey(traceQuotaGuardKey))
        {
            builder.Properties[traceQuotaGuardKey] = true;
            builder.RegisterServiceCollection(services =>
                services.AddSingleton<IHostedService>(sc =>
                {
                    var kiosk = sc.GetRequiredService<KioskOptions>();
                    return kiosk.Enabled
                        ? new NullHostedService()
                        : sc.GetRequiredService<TraceQuotaGuard>();
                }));
        }

        // Cost budgets. The guard is a singleton so the hosted-service registration and any direct
        // resolve share one instance, mirroring TraceQuotaGuard above.
        builder.Register(_ => new CostControlOptions())
            .As<CostControlOptions>()
            .SingleInstance()
            .IfNotRegistered(typeof(CostControlOptions));

        builder.RegisterType<CostStatistics>()
            .As<ICostStatistics>()
            .SingleInstance()
            .IfNotRegistered(typeof(CostStatistics));

        builder.RegisterType<CostBudgetGuard>()
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(CostBudgetGuard));

        const string costBudgetGuardKey = "Proxytrace.Application.CostBudgetGuard.Registered";
        if (!builder.Properties.ContainsKey(costBudgetGuardKey))
        {
            builder.Properties[costBudgetGuardKey] = true;
            builder.RegisterServiceCollection(services =>
                services.AddSingleton<IHostedService>(sc =>
                {
                    // The kiosk demo has no budgets to enforce and no operator to notify.
                    var kiosk = sc.GetRequiredService<KioskOptions>();
                    return kiosk.Enabled
                        ? new NullHostedService()
                        : sc.GetRequiredService<CostBudgetGuard>();
                }));
        }

        builder.RegisterType<AgentCallProcessor>()
            .As<IAgentCallProcessor>()
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(AgentCallProcessor));

        // Ingestion-time per-call outlier detection. Stateless apart from its stores (which resolve a
        // fresh DbContext per call), so a singleton is safe — mirrors AgentCallProcessor above.
        builder.RegisterType<Outliers.Internal.OutlierDetector>()
            .As<IOutlierDetector>()
            .SingleInstance()
            .IfNotRegistered(typeof(Outliers.Internal.OutlierDetector));

        // Shared in-process ingestion core. Used by the stream consumer per envelope AND directly by
        // same-process producers (Tracey) so they don't round-trip through the Redis transport.
        builder.RegisterType<IngestionExecutor>()
            .As<IIngestionExecutor>()
            .SingleInstance()
            .IfNotRegistered(typeof(IngestionExecutor));

        builder.RegisterType<AgentCallIngestionWorker>()
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(AgentCallIngestionWorker));

        const string agentCallIngestionWorkerKey = "Proxytrace.Application.AgentCallIngestionWorker.Registered";
        if (!builder.Properties.ContainsKey(agentCallIngestionWorkerKey))
        {
            builder.Properties[agentCallIngestionWorkerKey] = true;
            builder.RegisterServiceCollection(services =>
            {
                // Runs in every mode, including kiosk: kiosk uses the in-process ingestion stream
                // (see Proxytrace.Api.Module messaging config), so the worker consumes that channel
                // and persists captured calls (e.g. Tracey chats) into the in-memory demo DB.
                services.AddSingleton<IHostedService>(sc =>
                    sc.GetRequiredService<AgentCallIngestionWorker>());
            });
        }

        // Error log capture pipeline: a custom ILoggerProvider taps every Error/Critical log entry
        // into a bounded channel; ErrorLogWriter drains it to the ApplicationError table;
        // ErrorLogCleanupService rotates old rows + caps the table size.
        builder.RegisterType<ErrorLogChannel>()
            .As<IErrorLogChannel>()
            .SingleInstance()
            .IfNotRegistered(typeof(IErrorLogChannel));

        builder.Register(_ => new ErrorLogCleanupConfiguration())
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(ErrorLogCleanupConfiguration));

        builder.RegisterType<ErrorLogWriter>()
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(ErrorLogWriter));

        builder.RegisterType<ErrorLogCleanupService>()
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(ErrorLogCleanupService));

        const string errorLogKey = "Proxytrace.Application.ErrorLog.Registered";
        if (!builder.Properties.ContainsKey(errorLogKey))
        {
            builder.Properties[errorLogKey] = true;
            builder.RegisterServiceCollection(services =>
            {
                // The logging framework auto-discovers every ILoggerProvider in the container, so no
                // Program.cs change is needed to wire up capture.
                services.AddSingleton<ILoggerProvider>(sp =>
                    new ErrorLogChannelLoggerProvider(sp.GetRequiredService<IErrorLogChannel>()));
                services.AddHostedService(sp => sp.GetRequiredService<ErrorLogWriter>());
                services.AddHostedService(sp => sp.GetRequiredService<ErrorLogCleanupService>());
            });
        }

        // Audit log pipeline: a custom ILoggerProvider captures typed audit events logged through
        // ILogger<Audit> into a lossless unbounded channel; AuditWriter drains it to the AuditLogEntry
        // table; AuditLogCleanupService applies age-based retention. The actor is enriched from the
        // request context via IAuditActorAccessor (supplied by the API layer; absent host => System).
        builder.RegisterType<AuditChannel>()
            .As<IAuditChannel>()
            .SingleInstance()
            .IfNotRegistered(typeof(IAuditChannel));

        builder.Register(_ => new AuditLogCleanupConfiguration())
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(AuditLogCleanupConfiguration));

        builder.RegisterType<AuditWriter>()
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(AuditWriter));

        builder.RegisterType<AuditLogCleanupService>()
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(AuditLogCleanupService));

        const string auditLogKey = "Proxytrace.Application.AuditLog.Registered";
        if (!builder.Properties.ContainsKey(auditLogKey))
        {
            builder.Properties[auditLogKey] = true;
            builder.RegisterServiceCollection(services =>
            {
                // IAuditActorAccessor is resolved optionally: it is registered by the API layer over
                // IHttpContextAccessor; non-HTTP hosts (tests, kiosk) have none and audit falls back
                // to the System actor.
                services.AddSingleton<ILoggerProvider>(sp =>
                    new AuditChannelLoggerProvider(
                        sp.GetRequiredService<IAuditChannel>(),
                        sp.GetService<IAuditActorAccessor>()));
                services.AddHostedService(sp => sp.GetRequiredService<AuditWriter>());
                services.AddHostedService(sp => sp.GetRequiredService<AuditLogCleanupService>());

                // Pin the audit category enabled regardless of appsettings LogLevel configuration so
                // audit capture can never be silenced by log-level tuning.
                services.AddLogging(b => b.AddFilter<AuditChannelLoggerProvider>(
                    typeof(Audit).FullName, LogLevel.Information));
            });
        }

        builder.RegisterType<DataCleanupService>()
            .As<IDataCleanupService>()
            .SingleInstance();

        builder.RegisterType<SetupService>()
            .As<ISetupService>()
            .SingleInstance();

        builder.RegisterType<JitUserProvisioner>()
            .As<IJitUserProvisioner>()
            .SingleInstance();

        builder.RegisterType<UserAdministrationService>()
            .As<IUserAdministrationService>()
            .SingleInstance();

        builder.RegisterType<PasswordPolicy>()
            .As<IPasswordPolicy>()
            .SingleInstance();

        builder.RegisterType<StreamTicketService>()
            .As<IStreamTicketService>()
            .SingleInstance();

        // In-memory, single-use MFA challenge tickets — SingleInstance so the dictionary is shared.
        builder.RegisterType<MfaChallengeService>()
            .As<IMfaChallengeService>()
            .SingleInstance();

        builder.RegisterType<PasswordService>()
            .As<IPasswordService>()
            .SingleInstance();

        // Secret seams (ISecretProtector / ISecretHasher) + the Data Protection key ring now live in
        // Infrastructure (Infrastructure.Security.SecretProtectionModule) so the lean proxy — which must
        // not load Application — can register them too. The composition roots (API host, proxy host and
        // the test/perf harnesses) register that module directly, not Application.Module. See
        // docs/security.md (#270).

        builder.RegisterType<Notifications.Internal.SmtpEmailSender>()
            .As<Notifications.IEmailSender>()
            .SingleInstance();

        builder.Register(_ => new LocalAuthOptions
            {
                SigningKey = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
            })
            .As<LocalAuthOptions>()
            .SingleInstance()
            .IfNotRegistered(typeof(LocalAuthOptions));

        // Default auth options (e.g. EmergencyLogResetLink=false) so Application-layer consumers such as
        // PasswordResetService resolve even when no host binds the "Authentication" section. The API
        // registers the config-bound AuthOptions later, which wins. Mirrors LocalAuthOptions above.
        builder.Register(_ => new AuthOptions())
            .As<AuthOptions>()
            .SingleInstance()
            .IfNotRegistered(typeof(AuthOptions));

        builder.RegisterType<LocalTokenIssuer>()
            .As<ILocalTokenIssuer>()
            .SingleInstance();

        builder.RegisterType<InviteService>()
            .As<IInviteService>()
            .SingleInstance();

        builder.RegisterType<LoginService>()
            .As<ILoginService>()
            .SingleInstance();

        builder.RegisterType<LegacyClaimService>()
            .As<ILegacyClaimService>()
            .SingleInstance();

        builder.RegisterType<PasswordResetService>()
            .As<IPasswordResetService>()
            .SingleInstance();

        builder.RegisterType<TotpService>()
            .As<ITotpService>()
            .SingleInstance();

        builder.RegisterType<MfaService>()
            .As<IMfaService>()
            .SingleInstance();

        builder.RegisterInstance(Prompts.ResourceManager);

        builder.RegisterType<AgenticEvaluatorPresets>()
            .As<IAgenticEvaluatorPresets>()
            .SingleInstance();

        // Default: kiosk mode off. The API composition root registers the config-bound
        // instance (from the Kiosk section) before this module loads, which wins.
        builder.Register(_ => new KioskOptions())
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(KioskOptions));

        // Default: no real kiosk endpoint configured. The API composition root registers the
        // config-bound instance (from Kiosk:Endpoint) before this module loads, which wins.
        builder.Register(_ => new KioskEndpointOptions())
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(KioskEndpointOptions));

        builder.RegisterType<DemoSeedContext>()
            .AsSelf()
            .SingleInstance();

        var scenarioTypes = typeof(Module).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IDemoScenario).IsAssignableFrom(t));
        foreach (var t in scenarioTypes)
        {
            builder.RegisterType(t)
                .AsSelf()
                .As<IDemoScenario>()
                .SingleInstance()
                .IfNotRegistered(t);
        }

        // Per-scope: both resolve IAgentRepository, which is bound to the request's ambient
        // DbContext. A singleton would capture one repository/context and leak it across requests.
        builder.RegisterType<TraceyAgentProvisioner>()
            .As<ITraceyAgentProvisioner>()
            .InstancePerLifetimeScope();

        builder.RegisterType<TraceySessionService>()
            .As<ITraceySessionService>()
            .InstancePerLifetimeScope();

        builder.RegisterType<TraceyAgentSeederHostedService>()
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(TraceyAgentSeederHostedService));

        const string traceySeederHostedServiceKey = "Proxytrace.Application.TraceyAgentSeederHostedService.Registered";
        if (!builder.Properties.ContainsKey(traceySeederHostedServiceKey))
        {
            builder.Properties[traceySeederHostedServiceKey] = true;
            builder.RegisterServiceCollection(services =>
                services.AddSingleton<IHostedService>(sp =>
                    sp.GetRequiredService<TraceyAgentSeederHostedService>()));
        }

        // Per-scope: resolves IAgentRepository/IEvaluatorRepository, both bound to the request's
        // ambient DbContext. A singleton would capture one repository/context and leak it.
        builder.RegisterType<DefaultEvaluatorProvisioner>()
            .As<IDefaultEvaluatorProvisioner>()
            .InstancePerLifetimeScope();

        builder.RegisterType<DefaultEvaluatorSeederHostedService>()
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(DefaultEvaluatorSeederHostedService));

        const string defaultEvaluatorSeederHostedServiceKey = "Proxytrace.Application.DefaultEvaluatorSeederHostedService.Registered";
        if (!builder.Properties.ContainsKey(defaultEvaluatorSeederHostedServiceKey))
        {
            builder.Properties[defaultEvaluatorSeederHostedServiceKey] = true;
            builder.RegisterServiceCollection(services =>
                services.AddSingleton<IHostedService>(sp =>
                    sp.GetRequiredService<DefaultEvaluatorSeederHostedService>()));
        }

        builder.RegisterType<OrphanedTestRunReaperHostedService>()
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(OrphanedTestRunReaperHostedService));

        const string orphanedTestRunReaperHostedServiceKey = "Proxytrace.Application.OrphanedTestRunReaperHostedService.Registered";
        if (!builder.Properties.ContainsKey(orphanedTestRunReaperHostedServiceKey))
        {
            builder.Properties[orphanedTestRunReaperHostedServiceKey] = true;
            builder.RegisterServiceCollection(services =>
                services.AddSingleton<IHostedService>(sp =>
                    sp.GetRequiredService<OrphanedTestRunReaperHostedService>()));
        }

        builder.RegisterType<DemoSeederHostedService>()
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(DemoSeederHostedService));

        const string demoSeederHostedServiceKey = "Proxytrace.Application.DemoSeederHostedService.Registered";
        if (!builder.Properties.ContainsKey(demoSeederHostedServiceKey))
        {
            builder.Properties[demoSeederHostedServiceKey] = true;
            builder.RegisterServiceCollection(services =>
            {
                services.AddSingleton<IHostedService>(sp =>
                {
                    var kiosk = sp.GetRequiredService<KioskOptions>();
                    if (!kiosk.Enabled)
                    {
                        return new NullHostedService();
                    }

                    return sp.GetRequiredService<DemoSeederHostedService>();
                });
            });
        }
    }
}