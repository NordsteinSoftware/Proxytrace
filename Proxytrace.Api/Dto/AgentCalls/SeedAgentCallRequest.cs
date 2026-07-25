namespace Proxytrace.Api.Dto.AgentCalls;

/// <summary>
/// Test-only request to seed an agent call (trace) directly, bypassing the ingestion pipeline.
/// Used by the e2e suite to create traces without making real LLM calls.
/// </summary>
public record SeedAgentCallRequest(
    Guid AgentId,
    string Model,
    string UserContent,
    string AssistantContent,
    string? SystemContent,
    int InputTokens,
    int OutputTokens,
    int DurationMs,
    Guid? ConversationId,
    // Raw OutlierFlags bitmask to stamp on the seeded call (the seed bypasses ingestion, so the
    // detector never runs). Lets e2e create a deterministic outlier without building a baseline.
    int? OutlierFlags = null,
    // Tool names the assistant response "requested" — populates the per-call tool rows exactly as
    // ingestion would, so e2e can exercise the tool-name filter without a real tool-calling LLM.
    IReadOnlyList<string>? ToolNames = null,
    // Raw client session key: when set, the seeded call is stamped with the derived session id and
    // the session's counters are bumped, so e2e can exercise the sessions list / session filter.
    string? SessionKey = null);
