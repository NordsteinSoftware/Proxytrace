using Nordstein.Core.AI.Clients;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Proxytrace.Application.TestRun;
using Nordstein.Core.Common.DependencyInjection;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Proxytrace.Storage;
using Nordstein.Core.Testing;

namespace Proxytrace.PerfHarness.Bootstrap;

/// <summary>
/// Autofac module that boots the <b>real</b> Storage + Application graph against a <b>real
/// Postgres</b> database, so the perf harness exercises the exact code paths the running app does
/// (true indexes, <c>percentile_cont</c>, the real ingestion processor) — unlike the unit suite,
/// which runs on the in-memory provider.
/// <para>
/// It mirrors <c>Proxytrace.Application.Tests.Module</c> (the proven graph that resolves the whole
/// ingestion + statistics + repository stack) but points storage at Postgres and inlines the few
/// base registrations from the internal <c>Nordstein.Core.Testing.Module</c> (service provider, host
/// environment, logging, data protection). Infrastructure interfaces that would otherwise reach out
/// to a real LLM / SMTP / search backend are substituted: ingestion only parses already-captured
/// request/response bodies and persists them, so it never calls the model client.
/// </para>
/// </summary>
internal sealed class PerfModule : Autofac.Module
{
    private readonly string connectionString;

    public PerfModule(string connectionString)
    {
        this.connectionString = connectionString;
    }

    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        // --- base registrations mirrored from the internal Nordstein.Core.Testing.Module ---
        builder
            .Register(sp => new AutofacServiceProvider(sp.Resolve<ILifetimeScope>()))
            .As<IServiceProvider>();

        // Some services (DataProtection-backed secret protectors, license file discovery) resolve
        // IHostEnvironment when the EF model is built; a substitute pointed at the temp dir is enough.
        builder.RegisterStub<IHostEnvironment>(env =>
            env.ContentRootPath.Returns(Path.GetTempPath()));

        builder.RegisterServiceCollection(sc => sc.AddLogging());
        builder.RegisterServiceCollection(sc => sc.AddDataProtection());

        // --- real storage + application graph, pointed at Postgres ---
        builder.RegisterModule(new Storage.Module(_ => StorageConfiguration.Postgres(connectionString)));
        // #270: Storage.Module no longer references/registers Application; this composition root now
        // registers Application.Module and Infrastructure's secret seams explicitly (previously they
        // were pulled in transitively via Storage.Module).
        builder.RegisterModule<Proxytrace.Application.Module>();
        builder.RegisterModule<Proxytrace.Infrastructure.Security.SecretProtectionModule>();

        // --- substitute the infrastructure seams the data/ingestion graph depends on ---
        builder.RegisterStub<IModelClient>();
        builder.RegisterStub<IProviderClient>();
        builder.RegisterStub<Proxytrace.Application.Auth.ICurrentUserAccessor>();
        builder.RegisterStub<Proxytrace.Domain.Notifications.IEmailSettingsStore>();
        builder.RegisterStub<Proxytrace.Application.Notifications.IEmailSender>();
        builder.RegisterStub<Proxytrace.Application.Anomaly.IAnomalyDetectionService>();
        builder.RegisterStub<IAgentNameGenerator>(stub =>
            stub.GenerateNameAsync(Arg.Any<IPromptTemplate>(), Arg.Any<IProject>(), Arg.Any<CancellationToken>())
                .ReturnsForAnyArgs(Task.FromResult("Perf Agent")));

        // The ingestion processor, OpenAI parser and test runner are internal to Proxytrace.Application
        // and already registered by its real Module (registered explicitly above), so unlike
        // Application.Tests — which has InternalsVisibleTo and re-registers them — the harness just
        // relies on those registrations. ILogger<T> comes from the open-generic AddLogging above.
        builder.RegisterInstance(new TestRunnerConfiguration())
            .As<TestRunnerConfiguration>()
            .SingleInstance();

        builder.RegisterInstance(new Proxytrace.Application.Auth.Local.LocalAuthOptions
        {
            SigningKey = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
            Issuer = "proxytrace-local",
            Audience = "proxytrace-api",
            TokenLifetime = TimeSpan.FromDays(7),
        });
    }
}
