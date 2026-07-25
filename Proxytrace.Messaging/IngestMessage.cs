namespace Proxytrace.Messaging;

/// <summary>
/// Wire format for a captured proxy call handed from the ingestion proxy service to the
/// main app's ingestion worker. Holds only primitive identifiers and the raw bodies — the
/// consumer re-hydrates the <c>IModelProvider</c>/<c>IProject</c> from the shared database by id.
/// <para>
/// <c>AgentName</c>, when set, identifies the owning agent explicitly (e.g. via the
/// <c>X-Proxytrace-Agent</c> request header or a same-origin client like Tracey). The consumer then
/// attributes the call to that named agent directly, skipping the prompt/tool similarity matcher.
/// </para>
/// <para>
/// The <c>Blocked*</c> fields are set when the proxy rejected the call before it reached the
/// upstream provider (a blocking anomaly detector's trigger matched the request body).
/// <c>ResponseBody</c> then carries the synthetic error JSON the client received — the request was
/// never forwarded. These, along with <c>ConversationId</c>, are trailing optionals so older messages
/// already in the stream keep deserializing during a rolling deploy.
/// </para>
/// <para>
/// <c>SessionId</c> carries the client's <c>x-proxytrace-session-id</c> value — the debugging-session
/// key (one app run / user session, spanning agents and conversations). <c>ConversationId</c> carries
/// the <c>x-proxytrace-conversation-id</c> value — the thread key. When no conversation id is sent,
/// ingestion falls back to the session key for conversation grouping (byte-identical to pre-split
/// clients).
/// </para>
/// </summary>
public sealed record IngestMessage(
    Guid ProviderId,
    Guid ProjectId,
    string RequestBody,
    string? ResponseBody,
    long DurationMs,
    int HttpStatus,
    string? SessionId,
    string? AgentName = null,
    Guid? BlockedByDetectorId = null,
    string? BlockedDetectorName = null,
    string? BlockedTriggerPattern = null,
    string? ConversationId = null);
