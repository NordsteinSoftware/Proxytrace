using Nordstein.Core.AI.Clients;

namespace Proxytrace.Domain.ModelEndpoint;

/// <summary>
/// Creates a <see cref="IModelClient"/> bound to an agent and endpoint. Product-side seam:
/// carries the concerns the generic contract deliberately omits — the agent binding, an
/// endpoint override, and whether the call is recorded as a trace.
/// </summary>
public delegate IModelClient ModelClientFactory(
    Agent.IAgent agent,
    IModelEndpoint? customEndpoint = null,
    bool skipIngestion = false);
