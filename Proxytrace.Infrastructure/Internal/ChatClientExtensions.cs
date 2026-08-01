using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Proxytrace.Domain.Message;
// Aliased rather than imported: OpenAI.Chat also defines a ChatMessage, which would collide with
// Microsoft.Extensions.AI's own throughout this file.
using OpenAiChatOptions = OpenAI.Chat.ChatCompletionOptions;
using OpenAiReasoningEffort = OpenAI.Chat.ChatReasoningEffortLevel;
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
    /// rather than pinning it to a value the user never chose. Reasoning effort has no first-class
    /// <see cref="ChatOptions"/> member — see <see cref="ApplyReasoningEffort"/> for how it reaches
    /// the wire.
    /// <para>
    /// Nothing may be routed through <see cref="ChatOptions.AdditionalProperties"/>: the OpenAI
    /// adapter discards that dictionary wholesale, so a value put there is dropped in-process with
    /// no error. Anything without a first-class member goes the <see cref="ApplyReasoningEffort"/>
    /// way instead, and is covered by a test that reads the outgoing request body.
    /// </para>
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

        ApplyReasoningEffort(chatOptions, sampling.ReasoningEffort);
    }

    /// <summary>
    /// Puts the reasoning budget onto the request through the OpenAI client's own options type —
    /// the only route that actually reaches the provider.
    /// </summary>
    /// <remarks>
    /// It used to be written into <see cref="ChatOptions.AdditionalProperties"/> under its OpenAI
    /// wire name, on the assumption that a backend which did not understand it would answer with
    /// its own error. It never got the chance: the OpenAI adapter behind <see cref="IChatClient"/>
    /// maps the members it knows and <b>silently discards the dictionary</b>, so the field never
    /// left the process — the playground's Reasoning-effort control did nothing at all, which is
    /// the exact failure <see cref="ModelSamplingParameters"/> was introduced to end.
    /// <see cref="ChatOptions.RawRepresentationFactory"/> is the supported seam: the adapter starts
    /// from the instance returned here and then overwrites the members it maps itself, so what it
    /// does not know about — this one — survives onto the wire.
    /// </remarks>
    private static void ApplyReasoningEffort(ChatOptions chatOptions, string? effort)
    {
        if (string.IsNullOrWhiteSpace(effort))
        {
            return;
        }

        chatOptions.RawRepresentationFactory = _ =>
            // OPENAI001 marks the reasoning API as evaluation-only. It is the SDK's sole supported
            // way to set the parameter, and the value we send is a plain string either way.
#pragma warning disable OPENAI001
            new OpenAiChatOptions { ReasoningEffortLevel = new OpenAiReasoningEffort(effort) };
#pragma warning restore OPENAI001
    }
}