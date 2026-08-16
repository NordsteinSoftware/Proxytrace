using System.Net;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Nordstein.Core.AI.Tools;
using Nordstein.Core.Common.Random;
using Proxytrace.Domain.AgentCall;

namespace Proxytrace.Application.Demo.Internal;

/// <summary>
/// One planned agent call within a simulated interaction. <paramref name="RequestTail"/> holds the
/// request messages <em>after</em> the agent's system message (the executor prepends that, since
/// only it knows the agent); a <see langword="null"/> <paramref name="ResponseMessage"/> means the
/// call errored and has no completion.
/// </summary>
internal sealed record PlannedDemoCall(
    IReadOnlyList<Message> RequestTail,
    AssistantMessage? ResponseMessage,
    TokenUsage Usage,
    int LatencyMs,
    HttpStatusCode HttpStatus,
    string? ErrorMessage,
    string? FinishReason,
    OutlierFlags OutlierFlags,
    int OffsetSeconds);

/// <summary>
/// A simulated interaction: a single call, or a two-call tool round-trip whose calls share one
/// conversation id (<paramref name="SharesConversation"/>).
/// </summary>
internal sealed record DemoInteractionPlan(
    IReadOnlyList<PlannedDemoCall> Calls,
    bool SharesConversation);

/// <summary>
/// Samples one simulated interaction (content, token usage, latency, error/outlier dice) from a
/// <see cref="DemoTrafficCatalog.AgentTraffic"/> profile. This is the single source of the demo
/// traffic's statistical shape: the historical backfill and the live traffic feed both draw from
/// it, so "yesterday" and "right now" describe the same business.
/// </summary>
internal sealed class DemoCallPlanner
{
    private readonly IRandom random;

    public DemoCallPlanner(IRandom random)
    {
        this.random = random;
    }

    public DemoInteractionPlan Plan(DemoTrafficCatalog.AgentTraffic traffic)
    {
        bool isError = random.Double() < DemoTrafficCatalog.ErrorRate;
        bool isSpike = !isError && random.Double() < DemoTrafficCatalog.TokenSpikeRate;

        if (!isError && !isSpike && traffic.ToolStories.Length > 0 && random.Double() < traffic.ToolRate)
        {
            return PlanToolConversation(traffic);
        }

        return new DemoInteractionPlan([PlanSingleCall(traffic, isError, isSpike)], SharesConversation: false);
    }

    private PlannedDemoCall PlanSingleCall(DemoTrafficCatalog.AgentTraffic traffic, bool isError, bool isSpike)
    {
        var flags = OutlierFlags.None;
        string userText;
        string assistantText;
        ulong inTok;
        ulong outTok;
        ulong cachedIn;

        if (isSpike)
        {
            // A conversation that ballooned: far above the profile's baseline mean, with a pasted
            // wall of text in the request to match the numbers. The one-off paste also misses the
            // prompt cache.
            var spike = traffic.Spike;
            userText = spike.User;
            assistantText = spike.Assistant;
            inTok = (ulong)random.Int(spike.MinIn, spike.MaxIn + 1);
            outTok = (ulong)random.Int(spike.MinOut, spike.MaxOut + 1);
            cachedIn = 0;
            flags |= OutlierFlags.HighTokens;
        }
        else
        {
            (userText, assistantText) = random.Any(traffic.Pool);
            var shape = traffic.Text;
            inTok = (ulong)random.Int(shape.MinIn, shape.MaxIn + 1);
            outTok = (ulong)random.Int(shape.MinOut, shape.MaxOut + 1);

            // Most calls hit the prompt cache for part of the input; ~30% miss entirely, giving
            // the cache-hit KPI and distribution a realistic spread.
            cachedIn = CachedShare(inTok);
        }

        int latencyMs;
        if (random.Double() < DemoTrafficCatalog.LatencySpikeRate)
        {
            latencyMs = random.Int(4200, 9001);
            flags |= OutlierFlags.HighLatency;
        }
        else if (flags.HasFlag(OutlierFlags.HighTokens))
        {
            // A big context takes longer, but stays inside the unflagged latency tail.
            latencyMs = random.Int(1600, 3401);
        }
        else
        {
            latencyMs = random.Double() < DemoTrafficCatalog.LatencyTailRate
                ? random.Int(1500, 3501)
                : random.Int(400, 901);
        }

        var userMsg = new UserMessage([Content.FromText(userText)]);

        if (isError)
        {
            var variant = random.Any(DemoTrafficCatalog.ErrorVariants);
            return new PlannedDemoCall(
                RequestTail: [userMsg],
                ResponseMessage: null,
                Usage: new TokenUsage(0, 0),
                LatencyMs: latencyMs,
                HttpStatus: variant.Status,
                ErrorMessage: variant.Message,
                FinishReason: null,
                OutlierFlags: OutlierFlags.None,
                OffsetSeconds: 0);
        }

        return new PlannedDemoCall(
            RequestTail: [userMsg],
            ResponseMessage: new AssistantMessage([Content.FromText(assistantText)], []),
            Usage: new TokenUsage(inTok, outTok, cachedIn),
            LatencyMs: latencyMs,
            HttpStatus: HttpStatusCode.OK,
            ErrorMessage: null,
            FinishReason: "stop",
            OutlierFlags: flags,
            OffsetSeconds: 0);
    }

    private DemoInteractionPlan PlanToolConversation(DemoTrafficCatalog.AgentTraffic traffic)
    {
        var story = random.Any(traffic.ToolStories);
        string orderId = random.Int(10000, 100000).ToString();
        var user = new UserMessage([Content.FromText(story.User(orderId))]);

        var toolRequest = new ToolRequest(
            id: $"call_{story.ToolName}_{orderId}",
            name: story.ToolName,
            arguments: story.Arguments(orderId));
        var assistantToolMsg = new AssistantMessage([], [toolRequest]);

        var shape = traffic.ToolTurn;
        ulong toolTurnIn = (ulong)random.Int(shape.MinIn, shape.MaxIn + 1);
        var toolCall = new PlannedDemoCall(
            RequestTail: [user],
            ResponseMessage: assistantToolMsg,
            Usage: new TokenUsage(
                toolTurnIn,
                (ulong)random.Int(shape.MinOut, shape.MaxOut + 1),
                CachedShare(toolTurnIn)),
            LatencyMs: random.Int(380, 721),
            HttpStatus: HttpStatusCode.OK,
            ErrorMessage: null,
            FinishReason: "tool_calls",
            OutlierFlags: OutlierFlags.None,
            OffsetSeconds: 0);

        var toolMsg = new ToolMessage(new ToolResponse(
            toolRequest, [Content.FromText(story.ToolResult(orderId))]));
        var finalAssistant = new AssistantMessage([Content.FromText(story.Final(orderId))], []);

        // The answer turn re-sends the grown conversation (tool result included) and writes the
        // user-facing reply, so it carries more input and roughly twice the output of the request
        // turn.
        ulong finalTurnIn = toolTurnIn + (ulong)random.Int(120, 421);
        var answerCall = new PlannedDemoCall(
            RequestTail: [user, assistantToolMsg, toolMsg],
            ResponseMessage: finalAssistant,
            Usage: new TokenUsage(
                finalTurnIn,
                (ulong)random.Int(shape.MinOut * 2, shape.MaxOut * 2 + 1),
                CachedShare(finalTurnIn)),
            LatencyMs: random.Int(430, 821),
            HttpStatus: HttpStatusCode.OK,
            ErrorMessage: null,
            FinishReason: "stop",
            OutlierFlags: OutlierFlags.None,
            OffsetSeconds: random.Int(2, 9));

        return new DemoInteractionPlan([toolCall, answerCall], SharesConversation: true);
    }

    private ulong CachedShare(ulong inTok)
        => random.Double() < DemoTrafficCatalog.UncachedShareRate
            ? 0UL
            : (ulong)(inTok * random.Double(0.3, 0.8));
}
