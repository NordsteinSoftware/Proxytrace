using System.Net;
using AwesomeAssertions;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Application.CustomAnomaly;
using Proxytrace.Application.Ingestion;
using Proxytrace.Application.Notifications;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.CustomAnomaly;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.Project;
using Proxytrace.Messaging;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.CustomAnomaly;

/// <summary>
/// Ingestion of a proxy-blocked call: the trace persists flagged <see cref="OutlierFlags.Blocked"/>
/// with the detector attribution recorded, the SSE event and notification fire, and the call is
/// NOT enqueued for the post-hoc LLM review (there is no provider response to judge).
/// </summary>
[TestClass]
public sealed class BlockedCallIngestionTests : BaseTest<Module>
{
    private const string RequestBody = """
                                       {
                                           "model": "gpt-4o",
                                           "messages": [
                                               {"role": "system", "content": "You are a support agent."},
                                               {"role": "user", "content": "the admin password is hunter2"}
                                           ]
                                       }
                                       """;

    private const string BlockedResponseBody = """
        {"error":{"message":"Request blocked by Proxytrace anomaly detector 'Secret guard'.","type":"invalid_request_error","param":null,"code":"proxytrace_blocked"}}
        """;

    private static IngestMessage BlockedMessage(
        IModelProvider provider, IProject project, ICustomAnomalyDetector detector)
        => new(
            ProviderId: provider.Id,
            ProjectId: project.Id,
            RequestBody: RequestBody,
            ResponseBody: BlockedResponseBody,
            DurationMs: 3,
            HttpStatus: (int)HttpStatusCode.Forbidden,
            SessionId: null,
            AgentName: null,
            BlockedByDetectorId: detector.Id,
            BlockedDetectorName: detector.Name,
            BlockedTriggerPattern: "hunter2");

    private async Task<(IServiceProvider Services, ICustomAnomalyReviewQueue Queue,
        ICustomAnomalyBroadcaster Broadcaster, INotificationService Notifications,
        IModelProvider Provider, IProject Project, ICustomAnomalyDetector Detector)> BuildAsync()
    {
        var queue = Substitute.For<ICustomAnomalyReviewQueue>();
        var broadcaster = Substitute.For<ICustomAnomalyBroadcaster>();
        var notifications = Substitute.For<INotificationService>();
        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(queue).As<ICustomAnomalyReviewQueue>();
            builder.RegisterInstance(broadcaster).As<ICustomAnomalyBroadcaster>();
            builder.RegisterInstance(notifications).As<INotificationService>();
        });

        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>()
            .GetOrCreateAsync(CancellationToken);
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>()
            .GetOrCreateAsync(CancellationToken);
        var detector = await services.GetRequiredService<IDomainEntityGenerator<ICustomAnomalyDetector>>()
            .CreateAsync(CancellationToken);
        return (services, queue, broadcaster, notifications, provider, project, detector);
    }

    [TestMethod]
    public async Task IngestAsync_BlockedCall_PersistsCallWithBlockedFlag()
    {
        var (services, _, _, _, provider, project, detector) = await BuildAsync();
        var executor = services.GetRequiredService<IIngestionExecutor>();

        await executor.IngestAsync(BlockedMessage(provider, project, detector), CancellationToken);

        var call = (await services.GetRequiredService<IAgentCallRepository>().GetAllAsync(CancellationToken))
            .Should().ContainSingle().Subject;
        call.OutlierFlags.Should().Be(OutlierFlags.Blocked);
        call.HttpStatus.Should().Be(HttpStatusCode.Forbidden);
        call.Response.Should().BeNull("the request never reached the provider");
    }

    [TestMethod]
    public async Task IngestAsync_BlockedCall_PersistsDetectorAttribution()
    {
        var (services, _, _, _, provider, project, detector) = await BuildAsync();
        var executor = services.GetRequiredService<IIngestionExecutor>();

        await executor.IngestAsync(BlockedMessage(provider, project, detector), CancellationToken);

        var call = (await services.GetRequiredService<IAgentCallRepository>().GetAllAsync(CancellationToken))
            .Should().ContainSingle().Subject;
        var result = (await services.GetRequiredService<ICustomAnomalyResultRepository>().GetAllAsync(CancellationToken))
            .Should().ContainSingle().Subject;
        result.DetectorId.Should().Be(detector.Id);
        result.AgentCallId.Should().Be(call.Id);
        result.MatchedTrigger.Should().Be("hunter2");
        result.Reasoning.Should().Contain("Blocked at the proxy");
    }

    [TestMethod]
    public async Task IngestAsync_BlockedCall_BroadcastsBlockedEventAndNotifies()
    {
        var (services, _, broadcaster, notifications, provider, project, detector) = await BuildAsync();
        var executor = services.GetRequiredService<IIngestionExecutor>();

        await executor.IngestAsync(BlockedMessage(provider, project, detector), CancellationToken);

        broadcaster.Received(1).Publish(Arg.Is<AnomalyFlaggedEvent>(e =>
            e != null && e.Blocked && e.DetectorId == detector.Id && e.DetectorName == detector.Name));
        await notifications.Received(1).NotifyAsync(
            Arg.Is<NotificationRequest>(r => r != null && r.Kind == NotificationKind.Anomaly),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task IngestAsync_BlockedCall_DoesNotEnqueueLlmReview()
    {
        var (services, queue, _, _, provider, project, detector) = await BuildAsync();
        var executor = services.GetRequiredService<IIngestionExecutor>();

        await executor.IngestAsync(BlockedMessage(provider, project, detector), CancellationToken);

        await queue.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
