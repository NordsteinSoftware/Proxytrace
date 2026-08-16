using System.ComponentModel.DataAnnotations;
using System.Net;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentVersion;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.Domain;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.AgentCall.Internal;

internal record AgentCall : DomainEntity<IAgentCall>, IAgentCall
{
    /// <summary>
    /// Gets the agent.
    /// </summary>
    public IAgent Agent { get; }
    /// <summary>
    /// Gets the version.
    /// </summary>
    public IAgentVersion Version { get; }
    /// <summary>
    /// Gets the endpoint.
    /// </summary>
    public IModelEndpoint Endpoint { get; }
    /// <summary>
    /// Gets the request.
    /// </summary>
    public Conversation Request { get; }
    /// <summary>
    /// Gets the response.
    /// </summary>
    public ICompletion? Response { get; }
    /// <summary>
    /// Gets the http status.
    /// </summary>
    public HttpStatusCode HttpStatus { get; }
    /// <summary>
    /// Gets the finish reason.
    /// </summary>
    public string? FinishReason { get; }
    /// <summary>
    /// Gets the error message.
    /// </summary>
    public string? ErrorMessage { get; }
    /// <summary>
    /// Gets the model parameters.
    /// </summary>
    public IModelParameters ModelParameters { get; }
    /// <summary>
    /// Gets the conversation id.
    /// </summary>
    public Guid? ConversationId { get; }
    /// <summary>
    /// Gets the session id.
    /// </summary>
    public Guid? SessionId { get; }
    /// <summary>
    /// Gets the outlier flags.
    /// </summary>
    public OutlierFlags OutlierFlags { get; }
    /// <summary>
    /// Gets the api key id.
    /// </summary>
    public Guid? ApiKeyId { get; }
    /// <summary>
    /// Gets the project.
    /// </summary>
    public IProject Project => Agent.Project;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentCall"/> class.
    /// </summary>
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
        Guid? apiKeyId,
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
        ModelParameters = modelParameters ?? IModelParameters.Empty;
        ConversationId = conversationId;
        SessionId = sessionId;
        OutlierFlags = outlierFlags;
        ApiKeyId = apiKeyId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentCall"/> class.
    /// </summary>
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
        Guid? apiKeyId,
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
        ApiKeyId = apiKeyId;
    }

    /// <summary>
    /// Validates.
    /// </summary>
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
