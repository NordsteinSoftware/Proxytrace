using Autofac;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Application.Ingestion.Internal;
using Proxytrace.Application.Notifications;
using Proxytrace.Domain.Notifications;
using Proxytrace.Application.TestRun;
using Proxytrace.Application.TestRun.Internal;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Prompt;
using Proxytrace.Storage;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests;

public class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);
        builder.RegisterModule<Domain.Module>();
        builder.RegisterModule(new Storage.Module(_ => StorageConfiguration.InMemory()));
        // #270: Storage.Module no longer references/registers Application; this composition root now
        // registers Application.Module and Infrastructure's secret seams explicitly (previously these
        // were pulled in transitively via Storage.Module). The targeted overrides below still win as
        // they are registered afterwards.
        builder.RegisterModule<Proxytrace.Application.Module>();
        builder.RegisterModule<Proxytrace.Infrastructure.Security.SecretProtectionModule>();
        builder.RegisterModule<Proxytrace.Serialization.Module>();

        builder.RegisterStub<IModelClient>();
        builder.RegisterStub<IProviderClient>();
        builder.RegisterStub<Proxytrace.Application.Auth.ICurrentUserAccessor>();
        // EmailNotificationChannel needs IEmailSettingsStore and IEmailSender; stub both so that
        // tests resolving INotificationService (which fans out to all INotificationChannel instances
        // including EmailNotificationChannel) don't fail on missing IDataProtectionProvider.
        builder.RegisterStub<IEmailSettingsStore>();
        builder.RegisterStub<IEmailSender>();
        // TestRunnerService enqueues completed groups for anomaly detection; the real pipeline isn't
        // part of this test module, so stub it (tests asserting detection register their own).
        builder.RegisterStub<Proxytrace.Application.Anomaly.IAnomalyDetectionService>();

        builder.RegisterStub<IAgentNameGenerator>(stub =>
            stub.GenerateNameAsync(Arg.Any<IPromptTemplate>(), Arg.Any<IProject>(), Arg.Any<CancellationToken>())
                .ReturnsForAnyArgs(Task.FromResult("Test Agent")));

        builder.RegisterInstance(new TestRunnerConfiguration())
            .As<TestRunnerConfiguration>()
            .SingleInstance();

        builder.RegisterType<TestRunnerService>()
            .AsImplementedInterfaces()
            .SingleInstance();

        builder
            .Register(_ => NullLogger<TestRunnerService>.Instance)
            .As<Microsoft.Extensions.Logging.ILogger<TestRunnerService>>()
            .SingleInstance();

        builder.RegisterType<OpenAiCallParser>()
            .As<IOpenAiCallParser>()
            .SingleInstance();

        builder.RegisterType<AgentCallProcessor>()
            .AsSelf()
            .AsImplementedInterfaces()
            .SingleInstance();

        builder
            .Register(_ => NullLogger<AgentCallProcessor>.Instance)
            .As<Microsoft.Extensions.Logging.ILogger<AgentCallProcessor>>()
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
