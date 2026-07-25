using AwesomeAssertions;
using Proxytrace.Application.Streaming;
using Proxytrace.Application.Streaming.Internal;

namespace Proxytrace.Application.Tests.Streaming;

[TestClass]
public sealed class TraceBroadcasterTests
{
    public required TestContext TestContext { get; init; }

    [TestMethod]
    public async Task Subscribe_ThenPublish_DeliversToSubscriber()
    {
        using var broadcaster = new TraceBroadcaster();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reader = broadcaster.Subscribe(cts.Token);
        var evt = NewEvent();

        broadcaster.Publish(evt);

        var received = await reader.ReadAsync(cts.Token);
        received.Should().Be(evt);
    }

    [TestMethod]
    public async Task Publish_DeliversToAllSubscribers()
    {
        using var broadcaster = new TraceBroadcaster();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var r1 = broadcaster.Subscribe(cts.Token);
        var r2 = broadcaster.Subscribe(cts.Token);
        var evt = NewEvent();

        broadcaster.Publish(evt);

        (await r1.ReadAsync(cts.Token)).Should().Be(evt);
        (await r2.ReadAsync(cts.Token)).Should().Be(evt);
    }

    [TestMethod]
    public async Task Cancellation_CompletesReader()
    {
        using var broadcaster = new TraceBroadcaster();
        using var cts = new CancellationTokenSource();
        var reader = broadcaster.Subscribe(cts.Token);

        await cts.CancelAsync();

        var completion = reader.Completion;
        await Task.WhenAny(completion, Task.Delay(2000, cts.Token));
        completion.IsCompleted.Should().BeTrue();
    }

    [TestMethod]
    public void Publish_WithNoSubscribers_DoesNotThrow()
    {
        using TraceBroadcaster broadcaster = new TraceBroadcaster();

        // ReSharper disable once AccessToDisposedClosure
        var act = () => broadcaster.Publish(NewEvent());

        act.Should().NotThrow();
    }

    [TestMethod]
    public void Dispose_CompletesAllSubscribers()
    {
        var broadcaster = new TraceBroadcaster();
        using var cts = new CancellationTokenSource();
        var r1 = broadcaster.Subscribe(cts.Token);
        var r2 = broadcaster.Subscribe(cts.Token);

        broadcaster.Dispose();

        r1.Completion.IsCompleted.Should().BeTrue();
        r2.Completion.IsCompleted.Should().BeTrue();
    }

    private static TraceCreatedEvent NewEvent(Guid? sessionId = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "agent", "model", "provider", DateTimeOffset.UtcNow, null, sessionId);
}
