using System.ComponentModel.DataAnnotations;
using Proxytrace.Domain.AgentCall;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Message;

namespace Proxytrace.Domain.TestCase.Internal;

internal record TestCase : DomainEntity<ITestCase>, ITestCase
{
    public Conversation Input { get; }
    public AssistantMessage ExpectedOutput { get; }
    public Guid? SourceAgentCallId { get; }

    // CreateNewFromCall: promote a trace as-is — expected output is the response the agent recorded.
    public TestCase(
        IAgentCall agentCall,
        IRepository<ITestCase> repository) : this(
        agentCall.Request,
        agentCall.Response?.Response
            ?? throw new InvalidOperationException("Agent call response cannot be null when creating a test case."),
        agentCall.Id,
        repository)
    {
    }

    // CreateCorrection: record a human correction — the agent saw this input, and the right answer was
    // expectedOutput. Keeps the link back to the source trace so a rejected output becomes a regression
    // test with traceable provenance.
    public TestCase(
        IAgentCall agentCall,
        AssistantMessage expectedOutput,
        IRepository<ITestCase> repository) : this(
        agentCall.Request,
        expectedOutput,
        agentCall.Id,
        repository)
    {
    }

    // CreateNew: build directly from an input conversation + expected output. sourceAgentCallId is null
    // for a synthetic case, or the id of the trace it came from. (This carries the id explicitly rather
    // than overloading on (Conversation, AssistantMessage): IAgentCall is a container-resolvable entity,
    // so a bare (Conversation, AssistantMessage) ctor would tie with the CreateCorrection ctor and make
    // the Autofac delegate factory ambiguous — the distinct arity keeps each delegate unambiguous.)
    public TestCase(
        Conversation input,
        AssistantMessage expectedOutput,
        Guid? sourceAgentCallId,
        IRepository<ITestCase> repository) : base(repository)
    {
        Input = input.WithoutSystemMessage();
        ExpectedOutput = expectedOutput;
        SourceAgentCallId = sourceAgentCallId;
    }

    public TestCase(
        Conversation input,
        AssistantMessage expectedOutput,
        Guid? sourceAgentCallId,
        IDomainEntityData existing,
        IRepository<ITestCase> repository) : base(existing, repository)
    {
        Input = input;
        ExpectedOutput = expectedOutput;
        SourceAgentCallId = sourceAgentCallId;
    }

    public string GetSummary(int maxLength = 77)
    {
        var firstUser = Input.Messages.OfType<UserMessage>().FirstOrDefault();
        if (firstUser is null) return "Test case";
        var text = firstUser.GetText();
        return text.Length > maxLength + 3 ? text[..maxLength] + "…" : text;
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        foreach (var result in Input.Validate(validationContext))
        {
            yield return result;
        }

        if (Input.Messages.Any(x => x.Role == Role.System))
        {
            yield return new ValidationResult("Input conversation cannot contain system messages.", [nameof(Input)]);
        }
        
        foreach (var result in ExpectedOutput.Validate(validationContext))
        {
            yield return result;
        }
        
    }
}
