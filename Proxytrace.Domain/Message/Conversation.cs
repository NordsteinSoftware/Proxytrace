using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;

namespace Proxytrace.Domain.Message;

/// <summary>
/// Represents a conversation consisting of a sequence of messages.
/// </summary>
public sealed record Conversation : IDomainObject
{
    // Immutable backing list: assigned once in the constructor and never mutated. Every "mutation"
    // (With/WithSystemMessage/WithoutSystemMessage) returns a NEW Conversation, so a captured
    // instance can never change underfoot — important because this is a content-folding record
    // (Equals/GetHashCode fold the messages) used as a value object.
    private readonly IReadOnlyList<Message> messages;

    /// <summary>
    /// The messages in the conversation
    /// </summary>
    public IReadOnlyList<Message> Messages
        => messages;

    public SystemMessage? SystemMessage
        => Messages.FirstOrDefault(x => x.Role == Role.System) as SystemMessage;

    /// <summary>
    /// How many of the assistant's tool calls in this conversation already have their result
    /// recorded — calls that were both made and came back.
    ///
    /// This is what tells a *decision point* apart from a *summary*. An agent turn that uses tools
    /// is several upstream calls, each capturing the conversation so far; the last one already holds
    /// every tool call the agent made and every result it got, so the only thing a model can still
    /// produce from it is the closing message. Every choice the agent made was decided in an earlier
    /// call. A test case built on such a conversation therefore scores the wording of the summary,
    /// never the decision — see <c>docs/optimization-loop.md</c>.
    /// </summary>
    public int ResolvedToolCallCount
    {
        get
        {
            // A ToolMessage carries its tool-call id in the first content slot. Read that slot
            // directly instead of going through ToolMessage.Id, which throws on a malformed
            // message — a descriptive count is not worth failing a whole read over.
            HashSet<string> resolved =
            [
                .. Messages.OfType<ToolMessage>()
                    .Select(tool => tool.Contents.Count > 0 ? tool.Contents[0].Text : null)
                    .OfType<string>()
            ];
            return Messages.OfType<AssistantMessage>()
                .SelectMany(assistant => assistant.ToolRequests)
                .Count(request => resolved.Contains(request.Id));
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Conversation"/> class with the specified messages.
    /// </summary>
    /// <param name="messages">The messages in the conversation.</param>
    public Conversation(
        IReadOnlyList<Message> messages)
    {
        // Defensive copy so a caller mutating the passed-in list cannot mutate this value object.
        this.messages = messages.ToArray();
    }

    /// <summary>
    /// Creates a new empty conversation
    /// </summary>
    public static Conversation Create()
        => new([]);

    /// <summary>
    /// Returns a new conversation with <paramref name="message"/> appended.
    /// </summary>
    [Pure]
    public Conversation With(Message message)
    {
        if (message.Role == Role.System)
        {
            throw new InvalidOperationException("System messages must be added using WithSystemMessage");
        }
        return new Conversation([..messages, message]);
    }

    /// <summary>
    /// Returns a new conversation with <paramref name="systemMessage"/> prepended.
    /// </summary>
    [Pure]
    public Conversation WithSystemMessage(SystemMessage systemMessage)
    {
        if (messages.Any(x => x.Role == Role.System))
        {
            throw new InvalidOperationException("Conversation already contains a system message");
        }
        return new Conversation([systemMessage, ..messages]);
    }

    /// <summary>
    /// Returns the Conversation without the system message
    /// </summary>
    [Pure]
    public Conversation WithoutSystemMessage() 
        => new(Messages.Where(x => x.Role != Role.System).ToArray());

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        return Messages.SelectMany(x => x.Validate(validationContext));
    }

    /// <inheritdoc />
    public bool Equals(Conversation? other)
        => other is not null && 
           Messages.SequenceEqual(other.Messages);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        // Fold the messages' elements (Equals uses SequenceEqual); hashing the list reference would
        // give equal-content instances different hashes and break the Equals/GetHashCode contract.
        var hash = new HashCode();
        foreach (Message message in messages)
        {
            hash.Add(message);
        }
        return hash.ToHashCode();
    }
    
    /// <summary>
    /// Replaces any existing system Prompt with this system prompt
    /// </summary>
    [Pure]
    public static Conversation ReplaceSystemMessage(Conversation conversation, SystemMessage systemMessage)
        => conversation.WithoutSystemMessage().WithSystemMessage(systemMessage);

    public override string ToString() 
        => string.Join(Environment.NewLine, messages.Select(x => x.ToString()));
}