using Proxytrace.Domain.Notifications;
using Proxytrace.Domain.AuditLog;
using Autofac;
using Proxytrace.Application.AuditLog;
using Proxytrace.Application.Auth;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.User;
using Proxytrace.Storage;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

/// <summary>
/// Dependency injection module for API tests.
/// Wires TestRunnerService with in-memory storage and a stub IHttpClientFactory.
/// The factory is registered as a named constant so per-test overrides work cleanly.
/// </summary>
public sealed class Module : Autofac.Module
{
    private sealed class StubAgentNameGenerator : IAgentNameGenerator
    {
        public Task<string> GenerateNameAsync(
            IPromptTemplate systemPrompt,
            IProject project,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string>("Test Agent");
    }

    private sealed class StubCurrentUserAccessor : ICurrentUserAccessor
    {
        public Task<IUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IUser?>(null);
    }

    // Permissive by default (admin-equivalent: access everything), so existing controller tests that
    // don't care about tenant scoping keep passing. Tests that exercise cross-tenant authorization
    // register their own IProjectAccessGuard substitute via GetServices(builder => ...).
    private sealed class StubProjectAccessGuard : Api.Auth.IProjectAccessGuard
    {
        public Task<bool> CanAccessProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<IReadOnlyCollection<Guid>?> GetAccessibleProjectIdsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<Guid>?>(null);
    }

    // The real HttpContextAuditActorAccessor needs IHttpContextAccessor (registered by the API host,
    // not the test container). Stub it to the System actor, mirroring StubCurrentUserAccessor.
    private sealed class StubAuditActorAccessor : IAuditActorAccessor
    {
        public AuditActor GetCurrentActor() => AuditActor.System;
    }

    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder.RegisterModule<Api.Module>();
        builder.RegisterModule(new Storage.Module(_ => StorageConfiguration.InMemory()));
        builder.RegisterStub<IModelClient>();
        builder.RegisterStub<IProviderClient>();

        builder.RegisterInstance<IAgentNameGenerator>(new StubAgentNameGenerator())
            .SingleInstance();

        builder.RegisterInstance<ICurrentUserAccessor>(new StubCurrentUserAccessor())
            .SingleInstance();

        builder.RegisterInstance<Api.Auth.IProjectAccessGuard>(new StubProjectAccessGuard())
            .SingleInstance();

        builder.RegisterInstance<IAuditActorAccessor>(new StubAuditActorAccessor())
            .SingleInstance();

        // Register a default stub factory — tests override this via GetServices(action).
        builder
            .Register(_ => new FakeHttpClientFactory(FakeHttpMessageHandler.BuildOpenAiResponse("")))
            .As<IHttpClientFactory>()
            .SingleInstance();
    }
}
