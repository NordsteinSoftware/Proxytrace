using System.Text;
using Proxytrace.Domain.AgentCall;
using Nordstein.Core.AI.Messages;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Application.TestCase.Internal;

/// <summary>The model-facing rendering of a captured conversation, plus whether anything was cut.</summary>
internal sealed record SynthesisTranscript(string Text, bool Clipped);

/// <summary>
/// Renders a whole captured conversation — every call that shares a ConversationId — into the text
/// the synthesis agent reads. Pure; no I/O.
///
/// Two things make this more than a string join. First, every captured call re-contains the entire
/// prior conversation, so emitting each call's request verbatim would repeat the opening turn once
/// per call; only the messages added since the previous call are emitted. Second, a conversation has
/// no size limit — a tool result carrying a whole document would blow the context budget on its own —
/// so an oversized message is clipped by fair share (see <see cref="FairShareCap"/>), and the model
/// is told when that happened.
/// </summary>
internal static class ConversationTranscript
{
    /// <summary>Total characters of conversation text a transcript may spend (~15k tokens).</summary>
    internal const int TranscriptCharBudget = 60_000;

    /// <summary>Hard per-message ceiling (~1k tokens), applied on top of the fair share.</summary>
    internal const int MessageCharMax = 4_000;

    /// <summary>Max characters kept per tool description / JSON schema.</summary>
    private const int ToolSchemaCharMax = 600;

    internal static SynthesisTranscript Build(IReadOnlyList<IAgentCall> calls)
    {
        if (calls.Count == 0)
        {
            return new SynthesisTranscript(string.Empty, false);
        }

        int cap = Math.Min(
            MessageCharMax,
            FairShareCap([.. AllTextLengths(calls)], TranscriptCharBudget));

        bool clipped = false;
        string Take(string value)
        {
            string text = value.Trim();
            if (text.Length <= cap)
            {
                return text;
            }
            clipped = true;
            return $"{text[..cap]}…";
        }

        IAgentCall first = calls[0];
        var builder = new StringBuilder();
        builder.AppendLine($"AGENT: {first.Agent.Name}");
        builder.AppendLine("SYSTEM PROMPT:");
        builder.AppendLine(Take(first.Version.SystemPrompt.Template));
        builder.AppendLine();
        builder.AppendLine("TOOLS THE AGENT WAS OFFERED:");
        foreach (ToolSpecification tool in first.Version.Tools)
        {
            builder.AppendLine($"- {tool.Name}: {Clip(tool.Description, ToolSchemaCharMax)}");
            builder.AppendLine($"  arguments: {Clip(tool.Arguments.JsonSchema, ToolSchemaCharMax)}");
        }
        builder.AppendLine();
        builder.AppendLine($"CONVERSATION ({calls.Count} captured call(s))");

        // How many of the current call's request messages the transcript has already emitted. Each
        // call's request is the previous request + the previous response + whatever came next, so
        // this skips the repeated history. A shorter request means the thread branched — start over.
        int emitted = 0;
        foreach (IAgentCall call in calls)
        {
            IReadOnlyList<Message> messages = call.Request.Messages;
            if (messages.Count < emitted)
            {
                emitted = 0;
            }

            builder.AppendLine();
            builder.AppendLine(
                $"--- CALL agentCallId={call.Id} resolvedToolCalls={call.Request.ResolvedToolCallCount}");
            foreach (Message message in messages.Skip(emitted))
            {
                string rendered = Render(message, Take);
                if (rendered.Length > 0)
                {
                    builder.AppendLine(rendered);
                }
            }

            AssistantMessage? response = call.Response?.Response;
            if (response is not null)
            {
                builder.AppendLine($"RESPONSE: {Take(response.GetDisplayText())}");
            }

            emitted = messages.Count + (response is null ? 0 : 1);
        }

        if (clipped)
        {
            builder.AppendLine();
            builder.AppendLine(
                $"NOTE: oversized message text was clipped to {cap} characters to fit the context budget.");
        }

        return new SynthesisTranscript(builder.ToString(), clipped);
    }

    /// <summary>
    /// Largest per-string length that keeps <c>sum(min(length, cap))</c> within <paramref name="budget"/>
    /// — the classic fair-share (water-filling) split. Short messages survive intact and only the
    /// outsized ones are clipped, so one 200k-character tool result cannot starve the rest of the
    /// conversation. Returns <see cref="int.MaxValue"/> when everything fits.
    /// </summary>
    internal static int FairShareCap(IReadOnlyList<int> lengths, int budget)
    {
        long total = lengths.Sum(length => (long)length);
        if (total <= budget)
        {
            return int.MaxValue;
        }

        int remaining = budget;
        int unresolved = lengths.Count;
        foreach (int length in lengths.OrderBy(value => value))
        {
            int share = remaining / unresolved;
            if (length > share)
            {
                return share;
            }
            remaining -= length;
            unresolved -= 1;
        }
        return int.MaxValue;
    }

    private static IEnumerable<int> AllTextLengths(IReadOnlyList<IAgentCall> calls)
        => calls.SelectMany(call => call.Request.Messages
            .Select(message => message.GetText().Trim().Length)
            .Concat([call.Response?.Response?.GetDisplayText().Trim().Length ?? 0]));

    /// <summary>
    /// One transcript line per message. The system message renders empty on purpose — the system
    /// prompt is already printed once at the top, and repeating it per call is pure noise.
    /// </summary>
    private static string Render(Message message, Func<string, string> take)
        => message switch
        {
            AssistantMessage assistant => $"assistant: {take(assistant.GetDisplayText())}",
            ToolMessage tool => $"tool: {take(tool.GetText())}",
            SystemMessage => string.Empty,
            _ => $"{message.Role.ToString().ToLowerInvariant()}: {take(message.GetText())}",
        };

    private static string Clip(string value, int max)
    {
        string trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : $"{trimmed[..max]}…";
    }
}
