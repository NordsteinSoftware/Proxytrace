using System.Net;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Inference;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Search;

namespace Proxytrace.Domain.AgentCall;

/// <summary>
/// Records a single LLM call made by an agent, including request, response, token usage, and latency.
/// Calls are bound to a specific <see cref="IAgentVersion"/> so the version's exact prompt/tools at
/// the time of the call remain attached even if the agent is later edited.
/// </summary>
public interface IAgentCall : IDomainEntity<IAgentCall>, ISearchable
{
    /// <summary>The agent that initiated this call.</summary>
    IAgent Agent { get; }

    /// <summary>The agent version active at the time of the call.</summary>
    IAgentVersion Version { get; }

    IModelEndpoint Endpoint { get; }

    /// <summary>The conversation sent as the request.</summary>
    Conversation Request { get; }

    /// <summary>The assistant message returned as the response.</summary>
    ICompletion? Response { get; }

    /// <summary>HTTP status code returned by the provider.</summary>
    HttpStatusCode HttpStatus { get; }

    /// <summary>The model's stop reason, if provided (e.g. <c>end_turn</c>).</summary>
    string? FinishReason { get; }

    /// <summary>Error message if the call failed; otherwise <see langword="null"/>.</summary>
    string? ErrorMessage { get; }

    /// <summary>
    /// Sampling and decoding parameters extracted from this specific request.
    /// </summary>
    IModelParameters ModelParameters { get; }

    /// <summary>
    /// Groups this call with other calls from the same conversation thread. Derived from the
    /// <c>x-proxytrace-conversation-id</c> header, falling back to the <c>x-proxytrace-session-id</c>
    /// header when no conversation id is sent (so pre-split clients keep byte-identical grouping);
    /// otherwise detected via message-history matching.
    /// </summary>
    Guid? ConversationId { get; }

    /// <summary>
    /// Groups this call with all calls of the same debugging session (one app run / user
    /// session, possibly spanning multiple agents and conversations). Derived from the
    /// <c>x-proxytrace-session-id</c> header; null when the client sent none. FK-free correlation id —
    /// see ISession.
    /// </summary>
    Guid? SessionId { get; }

    /// <summary>
    /// Which per-call characteristics flagged this call as an outlier relative to its agent's recent
    /// baseline, computed once at ingestion. <see cref="OutlierFlags.None"/> means not an outlier.
    /// </summary>
    OutlierFlags OutlierFlags { get; }

    SearchKind ISearchable.SearchKind => SearchKind.AgentCall;

    public delegate IAgentCall CreateNew(
        IAgent agent,
        IAgentVersion version,
        IModelEndpoint endpoint,
        Conversation request,
        ICompletion? response,
        HttpStatusCode httpStatus = HttpStatusCode.OK,
        string? finishReason = null,
        string? errorMessage = null,
        IModelParameters? modelParameters = null,
        Guid? conversationId = null,
        Guid? sessionId = null,
        OutlierFlags outlierFlags = OutlierFlags.None);

    public delegate IAgentCall CreateExisting(
        IAgent agent,
        IAgentVersion version,
        IModelEndpoint endpoint,
        Conversation request,
        ICompletion? response,
        HttpStatusCode httpStatus,
        string? finishReason,
        string? errorMessage,
        IModelParameters modelParameters,
        IDomainEntityData existing,
        Guid? conversationId = null,
        Guid? sessionId = null,
        OutlierFlags outlierFlags = OutlierFlags.None);
}
