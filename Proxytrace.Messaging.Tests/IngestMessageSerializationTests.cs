using System.Text.Json;
using AwesomeAssertions;

namespace Proxytrace.Messaging.Tests;

[TestClass]
public sealed class IngestMessageSerializationTests
{
    [TestMethod]
    public void IngestMessage_RoundTripsThroughJson()
    {
        var original = new IngestMessage(
            ProviderId: Guid.NewGuid(),
            ProjectId: Guid.NewGuid(),
            RequestBody: """{"model":"gpt-4o","messages":[{"role":"user","content":"hi"}]}""",
            ResponseBody: """{"choices":[{"message":{"role":"assistant","content":"hello"}}]}""",
            DurationMs: 1234,
            HttpStatus: 200,
            SessionId: "session-123",
            AgentName: "Tracey");

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<IngestMessage>(json);

        restored.Should().Be(original);
    }

    [TestMethod]
    public void IngestMessage_RoundTripsWithNullOptionalFields()
    {
        var original = new IngestMessage(
            ProviderId: Guid.NewGuid(),
            ProjectId: Guid.NewGuid(),
            RequestBody: "{}",
            ResponseBody: null,
            DurationMs: 0,
            HttpStatus: 502,
            SessionId: null);

        var restored = JsonSerializer.Deserialize<IngestMessage>(JsonSerializer.Serialize(original));

        restored.Should().Be(original);
    }

    [TestMethod]
    public void IngestMessage_WithScopeKey_RoundTripsThroughJson()
    {
        var original = new IngestMessage(
            ProviderId: Guid.NewGuid(),
            ProjectId: Guid.NewGuid(),
            RequestBody: "{}",
            ResponseBody: null,
            DurationMs: 10,
            HttpStatus: 200,
            SessionId: null,
            ScopeKey: "support-agents");

        var restored = JsonSerializer.Deserialize<IngestMessage>(JsonSerializer.Serialize(original));

        restored.Should().Be(original);
    }

    [TestMethod]
    public void IngestMessage_PayloadFromBeforeScopes_DeserializesUnscoped()
    {
        // A message published by a proxy that predates scopes must still deserialize after a rolling
        // deploy — ScopeKey is a trailing optional and simply arrives null.
        var providerId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var json = $$"""
            {"ProviderId":"{{providerId}}","ProjectId":"{{projectId}}","RequestBody":"{}","ResponseBody":null,
             "DurationMs":10,"HttpStatus":200,"SessionId":null,"AgentName":null,"ApiKeyId":null}
            """;

        var restored = JsonSerializer.Deserialize<IngestMessage>(json);

        restored.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(restored);
        restored.ProjectId.Should().Be(projectId);
        restored.ScopeKey.Should().BeNull();
    }
}
