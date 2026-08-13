using System.Net;
using AwesomeAssertions;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Application.CustomAnomaly.Internal;
using Proxytrace.Application.Notifications;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.CustomAnomaly;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Notification;
using Proxytrace.Licensing;
using Proxytrace.Serialization;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.CustomAnomaly;

[TestClass]
public sealed class CustomAnomalyReviewServiceTests : BaseTest<Module>
{
    private const string AnomalousJson = """{"isAnomalous": true, "reasoning": "Promised an unauthorized refund."}""";
    private const string BenignJson = """{"isAnomalous": false, "reasoning": null}""";

    [TestMethod]
    public async Task ExecuteAsync_TriggerHitWithAnomalousVerdict_PersistsFlagsBroadcastsAndNotifies()
    {
        var (broadcaster, notifications, clients) = BuildCollaborators();
        var notified = new TaskCompletionSource<NotificationRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        notifications.NotifyAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var request = ci.Arg<NotificationRequest>();
                ArgumentNullException.ThrowIfNull(request);
                notified.TrySetResult(request);
                return Task.CompletedTask;
            });

        IServiceProvider services = GetServices(builder => Register(builder, LicenseOn(), broadcaster, notifications, clients));
        var detector = await CreateDetectorAsync(services, "Refund promises", "refund", isEnabled: true);
        clients.ByAgentId[detector.Agent.Id] = CannedClient(services, AnomalousJson);
        var call = await CreateCallAsync(services, "I demand a refund immediately!");

        // Drive the full background loop: enqueue → single-reader worker → notification (last step).
        var service = services.GetRequiredService<CustomAnomalyReviewService>();
        await service.StartAsync(CancellationToken);
        NotificationRequest request;
        try
        {
            await service.EnqueueAsync(call.Id, CancellationToken);
            request = await notified.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        // Result persisted.
        var results = await services.GetRequiredService<ICustomAnomalyResultRepository>()
            .GetByAgentCallIdsAsync([call.Id], CancellationToken);
        var result = results.Should().ContainSingle().Subject;
        result.DetectorId.Should().Be(detector.Id);
        result.ProjectId.Should().Be(call.Agent.Project.Id);
        result.MatchedTrigger.Should().Be("refund");
        result.Reasoning.Should().Be("Promised an unauthorized refund.");

        // Flag set on the call.
        var reloaded = await services.GetRequiredService<IAgentCallRepository>().GetAsync(call.Id, CancellationToken);
        reloaded.OutlierFlags.Should().HaveFlag(OutlierFlags.CustomAnomaly);

        // SSE event broadcast.
        broadcaster.Received(1).Publish(Arg.Is<AnomalyFlaggedEvent>(e =>
            e != null
            && e.AgentCallId == call.Id
            && e.AgentId == call.Agent.Id
            && e.ProjectId == call.Agent.Project.Id
            && e.DetectorId == detector.Id
            && e.DetectorName == detector.Name));

        // Notification raised with the call as target.
        request.Kind.Should().Be(NotificationKind.Anomaly);
        request.Severity.Should().Be(NotificationSeverity.Warning);
        request.ProjectId.Should().Be(call.Agent.Project.Id);
        request.TargetKind.Should().Be(NotificationTargetKind.AgentCall);
        request.TargetId.Should().Be(call.Id);

        // The judge client was created with skipIngestion — the guard against review recursion.
        clients.SkipIngestionFlags.Should().ContainSingle().Which.Should().BeTrue();
    }

    [TestMethod]
    public async Task ReviewAsync_NonAnomalousVerdict_PersistsNothing()
    {
        var (broadcaster, notifications, clients) = BuildCollaborators();
        IServiceProvider services = GetServices(builder => Register(builder, LicenseOn(), broadcaster, notifications, clients));
        var detector = await CreateDetectorAsync(services, "Refund promises", "refund", isEnabled: true);
        clients.ByAgentId[detector.Agent.Id] = CannedClient(services, BenignJson);
        var call = await CreateCallAsync(services, "I demand a refund immediately!");

        await ResolveService(services).ReviewAsync(call.Id, CancellationToken);

        await AssertNothingRecordedAsync(services, broadcaster, notifications, call.Id);
        clients.SkipIngestionFlags.Should().ContainSingle("the judge was consulted but ruled the turn benign");
    }

    [TestMethod]
    public async Task ReviewAsync_JudgeThrows_OtherDetectorsStillReview()
    {
        var (broadcaster, notifications, clients) = BuildCollaborators();
        IServiceProvider services = GetServices(builder => Register(builder, LicenseOn(), broadcaster, notifications, clients));
        var failing = await CreateDetectorAsync(services, "Failing detector", "refund", isEnabled: true);
        var working = await CreateDetectorAsync(services, "Working detector", "refund", isEnabled: true);
        clients.ByAgentId[failing.Agent.Id] = new ThrowingClient();
        clients.ByAgentId[working.Agent.Id] = CannedClient(services, AnomalousJson);
        var call = await CreateCallAsync(services, "I demand a refund immediately!");

        await ResolveService(services).ReviewAsync(call.Id, CancellationToken);

        var results = await services.GetRequiredService<ICustomAnomalyResultRepository>()
            .GetByAgentCallIdsAsync([call.Id], CancellationToken);
        results.Should().ContainSingle().Which.DetectorId.Should().Be(working.Id);
    }

    [TestMethod]
    public async Task ReviewAsync_DisabledDetector_IsSkipped()
    {
        var (broadcaster, notifications, clients) = BuildCollaborators();
        IServiceProvider services = GetServices(builder => Register(builder, LicenseOn(), broadcaster, notifications, clients));
        await CreateDetectorAsync(services, "Disabled detector", "refund", isEnabled: false);
        var call = await CreateCallAsync(services, "I demand a refund immediately!");

        await ResolveService(services).ReviewAsync(call.Id, CancellationToken);

        await AssertNothingRecordedAsync(services, broadcaster, notifications, call.Id);
        clients.SkipIngestionFlags.Should().BeEmpty("a disabled detector must never reach the judge");
    }

    [TestMethod]
    public async Task ReviewAsync_CallFromOutOfScopeAgent_IsSkipped()
    {
        var (broadcaster, notifications, clients) = BuildCollaborators();
        IServiceProvider services = GetServices(builder => Register(builder, LicenseOn(), broadcaster, notifications, clients));

        // Scope the detector to a different agent than the one making the call.
        var scopedAgent = await services.GetRequiredService<IAgentGenerator>()
            .CreateAsync("Other Agent", cancellationToken: CancellationToken);
        await CreateDetectorAsync(services, "Scoped detector", "refund", isEnabled: true, scopedAgents: [scopedAgent]);
        var call = await CreateCallAsync(services, "I demand a refund immediately!");

        await ResolveService(services).ReviewAsync(call.Id, CancellationToken);

        await AssertNothingRecordedAsync(services, broadcaster, notifications, call.Id);
        clients.SkipIngestionFlags.Should().BeEmpty("an out-of-scope call must never reach the judge");
    }

    [TestMethod]
    public async Task ReviewAsync_NoTriggerMatch_DoesNotInvokeJudge()
    {
        var (broadcaster, notifications, clients) = BuildCollaborators();
        IServiceProvider services = GetServices(builder => Register(builder, LicenseOn(), broadcaster, notifications, clients));
        await CreateDetectorAsync(services, "Refund promises", "refund", isEnabled: true);
        var call = await CreateCallAsync(services, "What is the weather like today?");

        await ResolveService(services).ReviewAsync(call.Id, CancellationToken);

        await AssertNothingRecordedAsync(services, broadcaster, notifications, call.Id);
        clients.SkipIngestionFlags.Should().BeEmpty("without a trigger hit there is no LLM review");
    }

    [TestMethod]
    public async Task ReviewAsync_FeatureNotLicensed_IsDormant()
    {
        var license = Substitute.For<ILicenseService>();
        license.IsFeatureEnabled(LicenseFeature.CustomAnomalyDetectors).Returns(false);
        var (broadcaster, notifications, clients) = BuildCollaborators();
        IServiceProvider services = GetServices(builder => Register(builder, license, broadcaster, notifications, clients));
        var detector = await CreateDetectorAsync(services, "Refund promises", "refund", isEnabled: true);
        clients.ByAgentId[detector.Agent.Id] = CannedClient(services, AnomalousJson);
        var call = await CreateCallAsync(services, "I demand a refund immediately!");

        await ResolveService(services).ReviewAsync(call.Id, CancellationToken);

        await AssertNothingRecordedAsync(services, broadcaster, notifications, call.Id);
        clients.SkipIngestionFlags.Should().BeEmpty("an unlicensed review pipeline is dormant");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static ILicenseService LicenseOn()
    {
        var license = Substitute.For<ILicenseService>();
        license.IsFeatureEnabled(LicenseFeature.CustomAnomalyDetectors).Returns(true);
        return license;
    }

    private static (ICustomAnomalyBroadcaster Broadcaster, INotificationService Notifications, ClientMap Clients) BuildCollaborators()
        => (Substitute.For<ICustomAnomalyBroadcaster>(), Substitute.For<INotificationService>(), new ClientMap());

    private static void Register(
        ContainerBuilder builder,
        ILicenseService license,
        ICustomAnomalyBroadcaster broadcaster,
        INotificationService notifications,
        ClientMap clients)
    {
        builder.RegisterInstance(license).As<ILicenseService>();
        builder.RegisterInstance(broadcaster).As<ICustomAnomalyBroadcaster>();
        builder.RegisterInstance(notifications).As<INotificationService>();
        builder.RegisterInstance<IModelClient.Factory>((agent, _, skipIngestion) => clients.Resolve(agent, skipIngestion));
    }

    private static CustomAnomalyReviewService ResolveService(IServiceProvider services)
        => services.GetRequiredService<CustomAnomalyReviewService>();

    private static IModelClient CannedClient(IServiceProvider services, string json)
        => new CannedJsonClient(json, services.GetRequiredService<IOutputFormat.Create>());

    private async Task<ICustomAnomalyDetector> CreateDetectorAsync(
        IServiceProvider services,
        string name,
        string phrase,
        bool isEnabled,
        IReadOnlyCollection<IAgent>? scopedAgents = null)
    {
        var judge = await services.GetRequiredService<IAgentGenerator>()
            .CreateAsync($"Judge {Guid.NewGuid():N}", isSystemAgent: true, cancellationToken: CancellationToken);
        var factory = services.GetRequiredService<ICustomAnomalyDetector.CreateNew>();
        var detector = factory(
            name, judge, [new AnomalyTrigger(TriggerKind.Phrase, phrase)],
            allAgents: scopedAgents is null, scopedAgents: scopedAgents ?? [], isEnabled: isEnabled, blockUpstream: false);
        return await services.GetRequiredService<ICustomAnomalyDetectorRepository>()
            .AddAsync(detector, CancellationToken);
    }

    private async Task<IAgentCall> CreateCallAsync(IServiceProvider services, string userText)
    {
        var agent = await services.GetRequiredService<IAgentGenerator>()
            .CreateAsync($"Agent {Guid.NewGuid():N}", cancellationToken: CancellationToken);
        var createCompletion = services.GetRequiredService<ICompletion.Create>();
        var completion = createCompletion(
            Message.CreateAssistantMessage([Content.FromText("Certainly, consider it done.")], []),
            null,
            TimeSpan.FromMilliseconds(10));
        var call = services.GetRequiredService<IAgentCall.CreateNew>()(
            agent: agent,
            version: agent.CurrentVersion,
            endpoint: agent.Endpoint,
            request: Conversation.Create().With(Message.CreateUserMessage(userText)),
            response: completion,
            httpStatus: HttpStatusCode.OK);
        return await services.GetRequiredService<IAgentCallRepository>().AddAsync(call, CancellationToken);
    }

    private async Task AssertNothingRecordedAsync(
        IServiceProvider services,
        ICustomAnomalyBroadcaster broadcaster,
        INotificationService notifications,
        Guid callId)
    {
        var results = await services.GetRequiredService<ICustomAnomalyResultRepository>()
            .GetByAgentCallIdsAsync([callId], CancellationToken);
        results.Should().BeEmpty();

        var reloaded = await services.GetRequiredService<IAgentCallRepository>().GetAsync(callId, CancellationToken);
        reloaded.OutlierFlags.Should().Be(OutlierFlags.None);

        broadcaster.DidNotReceive().Publish(Arg.Any<AnomalyFlaggedEvent>());
        await notifications.DidNotReceive().NotifyAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Per-test map from a judge agent's id to the canned client its <c>CreateClient</c> returns;
    /// records every requested <c>skipIngestion</c> flag for assertion.
    /// </summary>
    private sealed class ClientMap
    {
        public Dictionary<Guid, IModelClient> ByAgentId { get; } = [];
        public List<bool> SkipIngestionFlags { get; } = [];

        public IModelClient Resolve(IAgent agent, bool skipIngestion)
        {
            SkipIngestionFlags.Add(skipIngestion);
            return ByAgentId.TryGetValue(agent.Id, out var client)
                ? client
                : throw new InvalidOperationException($"No canned client registered for agent {agent.Id}.");
        }
    }

    /// <summary>
    /// Minimal typed-completion fake: parses a canned JSON response through the real
    /// <see cref="IOutputFormat"/>, so the caller's (private) verdict record never needs to be
    /// named by the test (mirrors the CannedJsonAgent optimizer fake).
    /// </summary>
    private sealed class CannedJsonClient : IModelClient
    {
        private readonly string cannedJson;
        private readonly IOutputFormat.Create outputFormatFactory;

        public CannedJsonClient(string cannedJson, IOutputFormat.Create outputFormatFactory)
        {
            this.cannedJson = cannedJson;
            this.outputFormatFactory = outputFormatFactory;
        }

        public Task<ICompletion> CompleteAsync(
            Conversation conversation,
            ModelOptions? options = null,
            IReadOnlyDictionary<string, string>? promptVariables = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ModelRequestPreview BuildRequestPreview(
            Conversation conversation,
            ModelOptions? options = null,
            IReadOnlyDictionary<string, string>? promptVariables = null)
            => throw new NotSupportedException();

        public async Task<TypedCompletion<TOutput>> CompleteAsync<TOutput>(
            Conversation conversation,
            ModelOptions? options = null,
            IReadOnlyDictionary<string, string>? promptVariables = null,
            CancellationToken cancellationToken = default)
        {
            var output = await outputFormatFactory(typeof(TOutput))
                .ParseAsync<TOutput>(cannedJson, cancellationToken);
            return new TypedCompletion<TOutput>(output, null, TimeSpan.FromMilliseconds(1));
        }

        public IAsyncEnumerable<ModelStreamUpdate> StreamAsync(
            SystemMessage systemMessage,
            Conversation conversation,
            ModelOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingClient : IModelClient
    {
        public Task<ICompletion> CompleteAsync(
            Conversation conversation,
            ModelOptions? options = null,
            IReadOnlyDictionary<string, string>? promptVariables = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("judge unavailable");

        public ModelRequestPreview BuildRequestPreview(
            Conversation conversation,
            ModelOptions? options = null,
            IReadOnlyDictionary<string, string>? promptVariables = null)
            => throw new InvalidOperationException("judge unavailable");

        public Task<TypedCompletion<TOutput>> CompleteAsync<TOutput>(
            Conversation conversation,
            ModelOptions? options = null,
            IReadOnlyDictionary<string, string>? promptVariables = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("judge unavailable");

        public IAsyncEnumerable<ModelStreamUpdate> StreamAsync(
            SystemMessage systemMessage,
            Conversation conversation,
            ModelOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("judge unavailable");

        public void Dispose()
        {
        }
    }
}
