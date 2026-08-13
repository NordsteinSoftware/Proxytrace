using AwesomeAssertions;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Proxytrace.Application.CustomAnomaly.Internal;
using Proxytrace.Application.Notifications;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.CustomAnomaly;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.Project;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.CustomAnomaly;

[TestClass]
public sealed class BlockedCallRecorderTests : BaseTest<Module>
{
    [TestMethod]
    public async Task RecordAsync_PersistsAttributionResult()
    {
        var broadcaster = Substitute.For<ICustomAnomalyBroadcaster>();
        var notifications = Substitute.For<INotificationService>();
        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(broadcaster).As<ICustomAnomalyBroadcaster>();
            builder.RegisterInstance(notifications).As<INotificationService>();
        });
        var recorder = services.GetRequiredService<IBlockedCallRecorder>();
        var call = FakeCall();
        var detectorId = Guid.NewGuid();

        await recorder.RecordAsync(call, detectorId, "Secret guard", "hunter2", CancellationToken);

        var result = (await services.GetRequiredService<ICustomAnomalyResultRepository>()
                .GetAllAsync(CancellationToken))
            .Should().ContainSingle().Subject;
        result.DetectorId.Should().Be(detectorId);
        result.AgentCallId.Should().Be(call.Id);
        result.MatchedTrigger.Should().Be("hunter2");
        broadcaster.Received(1).Publish(Arg.Is<AnomalyFlaggedEvent>(e => e != null && e.Blocked));
    }

    [TestMethod]
    public async Task RecordAsync_WhenAttributionPersistFails_StillBroadcastsAndNotifies()
    {
        // The detector may have been deleted between the proxy's cached match and ingestion — the
        // attribution row is then unpersistable, but the event + notification must still go out
        // (the Blocked flag on the call itself is set by the processor either way).
        var results = Substitute.For<ICustomAnomalyResultRepository>();
        results.AddAsync(Arg.Any<ICustomAnomalyResult>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("FK violation: detector gone"));
        var broadcaster = Substitute.For<ICustomAnomalyBroadcaster>();
        var notifications = Substitute.For<INotificationService>();

        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(results).As<ICustomAnomalyResultRepository>();
            builder.RegisterInstance(broadcaster).As<ICustomAnomalyBroadcaster>();
            builder.RegisterInstance(notifications).As<INotificationService>();
        });
        var recorder = services.GetRequiredService<IBlockedCallRecorder>();

        await recorder.RecordAsync(FakeCall(), Guid.NewGuid(), "Secret guard", "hunter2", CancellationToken);

        broadcaster.Received(1).Publish(Arg.Is<AnomalyFlaggedEvent>(e => e != null && e.Blocked));
        await notifications.Received(1).NotifyAsync(
            Arg.Is<NotificationRequest>(r => r != null && r.Kind == NotificationKind.Anomaly),
            Arg.Any<CancellationToken>());
    }

    private static IAgentCall FakeCall()
    {
        var project = Substitute.For<IProject>();
        project.Id.Returns(Guid.NewGuid());
        var agent = Substitute.For<IAgent>();
        agent.Id.Returns(Guid.NewGuid());
        agent.Name.Returns("Billing Agent");
        agent.Project.Returns(project);
        var call = Substitute.For<IAgentCall>();
        call.Id.Returns(Guid.NewGuid());
        call.Agent.Returns(agent);
        return call;
    }
}
