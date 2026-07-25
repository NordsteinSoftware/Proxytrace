using System.ComponentModel.DataAnnotations;
using System.Net;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Inference;
using Proxytrace.Domain.Internal;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.AgentCall.Internal;

internal record AgentCall : DomainEntity<IAgentCall>, IAgentCall
{
    public IAgent Agent { get; }
    public IAgentVersion Version { get; }
    public IModelEndpoint Endpoint { get; }
    public Conversation Request { get; }
    public ICompletion? Response { get; }
    public HttpStatusCode HttpStatus { get; }
    public string? FinishReason { get; }
    public string? ErrorMessage { get; }
    public IModelParameters ModelParameters { get; }
    public Guid? ConversationId { get; }
    public Guid? SessionId { get; }
    public OutlierFlags OutlierFlags { get; }
    public IProject Project => Agent.Project;

    public AgentCall(
        IAgent agent,
        IAgentVersion version,
        IModelEndpoint endpoint,
        Conversation request,
        ICompletion? response,
        HttpStatusCode httpStatus,
        string? finishReason,
        string? errorMessage,
        IModelParameters? modelParameters,
        Guid? conversationId,
        Guid? sessionId,
        OutlierFlags outlierFlags,
        IRepository<IAgentCall> repository) : base(repository)
    {
        Agent = agent;
        Version = version;
        Endpoint = endpoint;
        Request = request;
        Response = response;
        HttpStatus = httpStatus;
        FinishReason = finishReason;
        ErrorMessage = errorMessage;
        ModelParameters = modelParameters ?? Inference.Internal.ModelParameters.Empty;
        ConversationId = conversationId;
        SessionId = sessionId;
        OutlierFlags = outlierFlags;
    }

    public AgentCall(
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
        Guid? conversationId,
        Guid? sessionId,
        OutlierFlags outlierFlags,
        IRepository<IAgentCall> repository) : base(existing, repository)
    {
        Agent = agent;
        Version = version;
        Endpoint = endpoint;
        Request = request;
        Response = response;
        HttpStatus = httpStatus;
        FinishReason = finishReason;
        ErrorMessage = errorMessage;
        ModelParameters = modelParameters;
        ConversationId = conversationId;
        SessionId = sessionId;
        OutlierFlags = outlierFlags;
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        foreach (var result in Agent.Validate(validationContext))
        {
            yield return result;
        }

        foreach (var result in Version.Validate(validationContext))
        {
            yield return result;
        }

        foreach (var result in Endpoint.Validate(validationContext))
        {
            yield return result;
        }

        foreach (var result in Request.Validate(validationContext))
        {
            yield return result;
        }

        if (Response is not null)
        {
            foreach (var result in Response.Validate(validationContext))
            {
                yield return result;
            }
        }

        foreach (var result in ModelParameters.Validate(validationContext))
        {
            yield return result;
        }
    }
}
