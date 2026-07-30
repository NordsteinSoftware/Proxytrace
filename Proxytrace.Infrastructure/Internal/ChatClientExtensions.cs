using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.ModelEndpoint;

namespace Proxytrace.Infrastructure.Internal;

internal static class ChatClientExtensions
{
    public static IEnumerable<ChatMessage> ToChatMessages(this Conversation conversation)
        => conversation.Messages.Select(m => m.ToChatMessage());

    public static ChatMessage ToChatMessage(this Message message)
    {
        ChatRole role = message.Role switch
        {
            Role.User => ChatRole.User,
            Role.Assistant => ChatRole.Assistant,
            Role.Tool => ChatRole.Tool,
            Role.System => ChatRole.System,
            _ => throw new InvalidOperationException($"Unknown role: {message.Role}")
        };

        if (message is AssistantMessage { ToolRequests.Count: > 0 } assistantMessage)
        {
            var aiContents = new List<AIContent>();
            var text = BuildText(message.Contents);
            if (!string.IsNullOrEmpty(text))
                aiContents.Add(new TextContent(text));
            foreach (var req in assistantMessage.ToolRequests)
            {
                var args = JsonSerializer.Deserialize<IDictionary<string, object?>>(req.Arguments);
                aiContents.Add(new FunctionCallContent(req.Id, req.Name, args));
            }
            return new ChatMessage(role, aiContents);
        }

        if (message is ToolMessage toolMessage)
        {
            var (id, contents) = toolMessage.Deconstruct();
            return new ChatMessage(role, [new FunctionResultContent(id, BuildText(contents))]);
        }

        return new ChatMessage(role, BuildText(message.Contents));
    }

    private static string BuildText(IReadOnlyList<Content> contents)
    {
        var sb = new StringBuilder();
        foreach (var content in contents)
        {
            if (content is { Kind: ContentKind.Text, Text: not null })
                sb.AppendLine(content.Text);
            else if (content.Kind == ContentKind.Image)
                throw new NotSupportedException("Image content is not supported in chat messages yet");
        }
        return sb.ToString().Trim();
    }

    public static ChatOptions ToChatOptions(this ModelOptions options)
    {
        var chatOptions = new ChatOptions { ModelId = options.ModelName };

        if (options.Tools.Any())
        {
            chatOptions.Tools = options.Tools
                .Select(t =>
                {
                    // JsonDocument rents from a pooled buffer; Clone() detaches a standalone copy so
                    // the document can be disposed immediately, returning the buffer rather than
                    // leaking it on every tool of every model request.
                    using var doc = JsonDocument.Parse(t.Arguments.JsonSchema);
                    return (AITool)AIFunctionFactory.CreateDeclaration(
                        t.Name,
                        t.Description,
                        doc.RootElement.Clone());
                })
                .ToList();
        }

        ApplySampling(chatOptions, options.Sampling);
        return chatOptions;
    }

    /// <summary>
    /// Copies the caller's sampling overrides onto the outgoing request.
    /// </summary>
    /// <remarks>
    /// Only non-null members are set, so an unset override leaves the provider's default alone
    /// rather than pinning it to a value the user never chose. Reasoning effort and choice count
    /// have no first-class <see cref="ChatOptions"/> member, so they go through
    /// <see cref="ChatOptions.AdditionalProperties"/> under their OpenAI wire names; a backend that
    /// does not understand them answers with its own error, which is more useful than dropping the
    /// value silently — the behaviour this whole method exists to fix.
    /// </remarks>
    private static void ApplySampling(ChatOptions chatOptions, ModelSamplingParameters? sampling)
    {
        if (sampling is null || sampling.IsEmpty)
        {
            return;
        }

        if (sampling.Temperature is { } temperature) chatOptions.Temperature = (float)temperature;
        if (sampling.TopP is { } topP) chatOptions.TopP = (float)topP;
        if (sampling.FrequencyPenalty is { } frequency) chatOptions.FrequencyPenalty = (float)frequency;
        if (sampling.PresencePenalty is { } presence) chatOptions.PresencePenalty = (float)presence;
        if (sampling.MaxOutputTokens is { } maxTokens) chatOptions.MaxOutputTokens = maxTokens;
        if (sampling.Seed is { } seed) chatOptions.Seed = seed;
        if (sampling.StopSequences is { Count: > 0 } stop) chatOptions.StopSequences = [.. stop];

        if (!string.IsNullOrWhiteSpace(sampling.ReasoningEffort))
        {
            (chatOptions.AdditionalProperties ??= [])["reasoning_effort"] = sampling.ReasoningEffort;
        }

        if (sampling.ChoiceCount is { } choiceCount)
        {
            (chatOptions.AdditionalProperties ??= [])["n"] = choiceCount;
        }
    }
}