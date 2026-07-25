using System.Threading.Channels;
using Proxytrace.Domain.AgentCall;

namespace Proxytrace.Application.Streaming;

public record TraceCreatedEvent(
    Guid Id,
    Guid AgentId,
    Guid ProjectId,
    string AgentName,
    string Model,
    string Provider,
    DateTimeOffset CreatedAt,
    Guid? ConversationId,
    Guid? SessionId)
{
    public static TraceCreatedEvent Create(IAgentCall call)
        => new(
            call.Id,
            call.Agent.Id,
            call.Agent.Project.Id,
            call.Agent.Name,
            call.Endpoint.Model.Name,
            call.Endpoint.Provider.Name,
            call.CreatedAt,
            call.ConversationId,
            call.SessionId);
}

public interface ITraceBroadcaster
{
    ChannelReader<TraceCreatedEvent> Subscribe(CancellationToken cancellationToken);
    
    void Publish(TraceCreatedEvent evt);
}