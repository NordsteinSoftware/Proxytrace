using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Proxytrace.Messaging.Internal;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Proxytrace.Messaging.Tests;

/// <summary>
/// Wire-level coverage for <see cref="RedisIngestionStream"/> against a real Redis.
/// <para>
/// The sibling <see cref="RedisIngestionStreamTests"/> mocks <see cref="IDatabase"/>, so it asserts
/// how we <em>call</em> StackExchange.Redis and never how Redis <em>replies</em>. That leaves the
/// reply-parsing half of the transport untested: a full RESP2→RESP3 protocol switch (which changes
/// the shape of the <c>XINFO GROUPS</c> and <c>XAUTOCLAIM</c> replies this class reads) passed the
/// mocked suite unchanged. These tests run the whole contract through a real server on whatever
/// protocol the referenced package negotiates, so a version bump that flips the wire format shows up
/// here instead of in production.
/// </para>
/// <para>
/// The queue-depth assertions matter most: <see cref="RedisIngestionStream.GetQueueDepthAsync"/>
/// deliberately swallows <see cref="RedisException"/> because depth is observability-only, so a
/// broken <c>XINFO GROUPS</c> reply degrades to a permanent depth of <c>0</c> — a dashboard
/// reporting a healthy backlog while the consumer falls behind. Only a non-zero expectation against
/// a real server distinguishes a parsed reply from a swallowed exception.
/// </para>
/// </summary>
[TestClass]
public sealed class RedisIngestionStreamIntegrationTests
{
    /// <summary>Pinned to the image the deployed stack runs (see <c>docker-compose.yml</c>).</summary>
    private const string RedisImage = "redis:7-alpine";

    public required TestContext TestContext { get; init; }

    /// <summary>
    /// Whether a container runtime must be present. Locally these tests skip when Docker is absent —
    /// the backend suite must not become a hard Docker dependency for every <c>dotnet test</c> run.
    /// CI sets this so the coverage can never be lost silently, which is the exact failure mode
    /// (a suite that reports success while asserting nothing) these tests exist to close.
    /// </summary>
    private static bool DockerRequired
        => Environment.GetEnvironmentVariable("PROXYTRACE_REQUIRE_DOCKER_TESTS") is { } value
           && (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

    // The full producer→consumer contract in one round trip: XADD, XREADGROUP (via the reclaim +
    // read consume loop), XACK, and both XINFO GROUPS readings of the group's lag.
    [TestMethod]
    public async Task PublishConsumeAckAndDepth_AgainstRealRedis_RoundTripsTheWholeContract()
    {
        await using RedisContainer redis = await StartRedisAsync(TestContext.CancellationToken);
        var configuration = new MessagingConfiguration { RedisConnectionString = redis.GetConnectionString() };
        await using ConnectionMultiplexer connection = await ConnectAsync(configuration, TestContext.CancellationToken);

        var stream = new RedisIngestionStream(
            connection, configuration, NullLogger<RedisIngestionStream>.Instance);

        IngestMessage first = Message("one");
        IngestMessage second = Message("two");
        await stream.PublishAsync(first, TestContext.CancellationToken);
        await stream.PublishAsync(second, TestContext.CancellationToken);

        List<IngestEnvelope> received = await ConsumeAsync(stream, count: 2, TestContext.CancellationToken);

        // Payloads survive the serialize → XADD → XREADGROUP → deserialize round trip in order, and
        // every envelope carries the stream entry id the consumer later acks by.
        received.Select(envelope => envelope.Message).Should().Equal(first, second);
        received.Should().OnlyContain(envelope => !string.IsNullOrWhiteSpace(envelope.MessageId));

        foreach (IngestEnvelope envelope in received)
        {
            await stream.AckAsync(envelope.MessageId, TestContext.CancellationToken);
        }

        long drained = await stream.GetQueueDepthAsync(TestContext.CancellationToken);
        drained.Should().Be(0L, "everything published has been delivered to the group and acknowledged");

        // The load-bearing assertion: entries added but never delivered must be reported as backlog.
        // A misread XINFO GROUPS reply throws, is swallowed as observability-only, and returns 0 —
        // indistinguishable from a drained queue unless the expectation is non-zero.
        await stream.PublishAsync(Message("three"), TestContext.CancellationToken);
        await stream.PublishAsync(Message("four"), TestContext.CancellationToken);

        long backlog = await stream.GetQueueDepthAsync(TestContext.CancellationToken);
        backlog.Should().Be(2L, "two entries were added to the stream and never delivered to the group");
    }

    // The crash-recovery path: an entry a dead consumer left pending is only ever redelivered by
    // XAUTOCLAIM, whose non-empty reply (StreamAutoClaimResult) the round trip above never produces
    // because nothing is ever left pending there.
    [TestMethod]
    public async Task ConsumeAsync_WithEntryPendingOnADeadConsumer_ReclaimsItViaAutoClaim()
    {
        await using RedisContainer redis = await StartRedisAsync(TestContext.CancellationToken);
        var connectionString = redis.GetConnectionString();
        var dead = new MessagingConfiguration
        {
            RedisConnectionString = connectionString,
            ConsumerName = "dead-consumer",
        };
        await using ConnectionMultiplexer connection = await ConnectAsync(dead, TestContext.CancellationToken);

        var deadStream = new RedisIngestionStream(
            connection, dead, NullLogger<RedisIngestionStream>.Instance);

        IngestMessage message = Message("orphaned");
        await deadStream.PublishAsync(message, TestContext.CancellationToken);

        // Delivered but never acked — as if the consumer crashed mid-persist.
        List<IngestEnvelope> abandoned = await ConsumeAsync(deadStream, count: 1, TestContext.CancellationToken);
        abandoned.Should().ContainSingle();

        // A replacement consumer: XREADGROUP with NewMessages cannot see an already-delivered entry,
        // so anything it yields came through XAUTOCLAIM. Idle threshold 0 makes the entry eligible
        // immediately instead of after the production reclaim window.
        var replacement = new MessagingConfiguration
        {
            RedisConnectionString = connectionString,
            ConsumerName = "replacement-consumer",
            ReclaimIdleMs = 0,
        };
        var replacementStream = new RedisIngestionStream(
            connection, replacement, NullLogger<RedisIngestionStream>.Instance);

        List<IngestEnvelope> reclaimed = await ConsumeAsync(replacementStream, count: 1, TestContext.CancellationToken);

        reclaimed.Should().ContainSingle();
        reclaimed[0].Message.Should().Be(message);
        reclaimed[0].MessageId.Should().Be(abandoned[0].MessageId, "the same stream entry was reclaimed");
    }

    private static async Task<RedisContainer> StartRedisAsync(CancellationToken cancellationToken)
    {
        RedisContainer container = new RedisBuilder(RedisImage).Build();
        try
        {
            await container.StartAsync(cancellationToken);
        }
        // Docker unavailable surfaces as anything from a socket-level HttpRequestException to a
        // Testcontainers-specific failure depending on how the runtime is (mis)configured, so the
        // skip cannot key off a single exception type. The filter keeps the catch honest: where a
        // runtime is guaranteed (CI), nothing is swallowed and the failure is reported as-is.
        catch (Exception ex) when (!DockerRequired)
        {
            await container.DisposeAsync();
            Assert.Inconclusive(
                $"Skipping the real-Redis transport test — no usable container runtime: {ex.Message}");
        }

        return container;
    }

    private static async Task<ConnectionMultiplexer> ConnectAsync(
        MessagingConfiguration configuration,
        CancellationToken cancellationToken)
    {
        // Mirrors the multiplexer the Redis composition root builds (see Messaging.Module), so the
        // IsConnected short-circuits in PublishAsync/GetQueueDepthAsync are exercised as configured.
        ConfigurationOptions options = ConfigurationOptions.Parse(configuration.RedisConnectionString);
        options.AbortOnConnectFail = false;
        ConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(options);

        // With AbortOnConnectFail=false the connect never throws and can return before the handshake
        // lands. Both PublishAsync and GetQueueDepthAsync no-op while IsConnected is false, so racing
        // ahead here would publish nothing and still see a depth of 0 — a green run asserting nothing.
        for (var attempt = 0; attempt < 50 && !connection.IsConnected; attempt++)
        {
            await Task.Delay(100, cancellationToken);
        }

        connection.IsConnected.Should().BeTrue("the test Redis container must be reachable");
        return connection;
    }

    private static async Task<List<IngestEnvelope>> ConsumeAsync(
        IIngestionStream stream,
        int count,
        CancellationToken cancellationToken)
    {
        // The consume loop polls until cancelled. Bound the wait so a reply the transport can no
        // longer parse fails as a missing envelope in seconds rather than hanging the run.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));

        var received = new List<IngestEnvelope>();
        try
        {
            await foreach (IngestEnvelope envelope in stream.ConsumeAsync(deadline.Token))
            {
                received.Add(envelope);
                if (received.Count == count)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Deadline elapsed — fall through so the caller's assertion reports what was missing.
        }

        return received;
    }

    private static IngestMessage Message(string marker) => new(
        ProviderId: Guid.NewGuid(),
        ProjectId: Guid.NewGuid(),
        RequestBody: marker,
        ResponseBody: null,
        DurationMs: 1,
        HttpStatus: 200,
        SessionId: null);
}
