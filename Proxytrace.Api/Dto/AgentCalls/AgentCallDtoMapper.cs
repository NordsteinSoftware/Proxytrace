using System.Text.RegularExpressions;
using Proxytrace.Api.Dto.Inference;
using Proxytrace.Api.Dto.Tools;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Message;

namespace Proxytrace.Api.Dto.AgentCalls;

/// <summary>
/// Maps <see cref="IAgentCall"/> domain entities to <see cref="AgentCallDto"/>.
/// Shared by the agent-calls controller and aggregate view endpoints.
/// </summary>
public sealed class AgentCallDtoMapper
{
    private readonly ToolDtoMapper toolDtoMapper;

    public AgentCallDtoMapper(ToolDtoMapper toolDtoMapper)
    {
        this.toolDtoMapper = toolDtoMapper;
    }

    public AgentCallDto ToDto(IAgentCall c) => new(
        c.Id,
        c.Agent.Id,
        c.Agent.Name,
        c.Endpoint.Model.Name,
        c.Endpoint.Provider.Name,
        c.Request.Messages.Select(ToMessageDto).ToArray(),
        c.Response != null ? ToMessageDto(c.Response.Response) : null,
        [.. c.Agent.Tools.Select(toolDtoMapper.ToToolSpecDto)],
        c.Response?.Usage?.InputTokenCount,
        c.Response?.Usage?.OutputTokenCount,
        c.Response?.Usage?.CachedInputTokenCount,
        c.Response?.Latency.TotalMilliseconds,
        (int)c.HttpStatus,
        c.FinishReason,
        c.ErrorMessage,
        ComputeCost(c),
        ModelParametersDto.FromDomain(c.ModelParameters),
        c.CreatedAt,
        c.UpdatedAt,
        c.ConversationId,
        c.SessionId,
        (int)c.OutlierFlags);

    /// <summary>
    /// Maps the storage-projected <see cref="AgentCallListItem"/> (traces table list path) straight
    /// to its DTO — the preview and tool count are already precomputed, so nothing is deserialised.
    /// </summary>
    public AgentCallListItemDto ToListItemDto(AgentCallListItem c) => new(
        c.Id,
        c.AgentId,
        c.AgentName,
        c.ModelName,
        c.ProviderName,
        c.MessagePreview,
        c.ToolCount,
        c.InputTokens,
        c.OutputTokens,
        c.CachedInputTokens,
        c.LatencyMs,
        c.HttpStatus,
        c.FinishReason,
        c.ErrorMessage,
        c.Cost,
        c.CreatedAt,
        c.UpdatedAt,
        c.ConversationId,
        c.SessionId,
        (int)c.OutlierFlags);

    /// <summary>
    /// Lightweight projection for the dashboard recent-traces strip (a small fixed count) from a
    /// fully-loaded domain entity — row fields plus the precomputed message preview and response
    /// tool-request count, skipping the full request, response, tool specs and model parameters.
    /// </summary>
    public AgentCallListItemDto ToListItemDto(IAgentCall c) => new(
        c.Id,
        c.Agent.Id,
        c.Agent.Name,
        c.Endpoint.Model.Name,
        c.Endpoint.Provider.Name,
        FirstUserMessage(c),
        c.Response?.Response is AssistantMessage a ? a.ToolRequests.Count : 0,
        c.Response?.Usage?.InputTokenCount,
        c.Response?.Usage?.OutputTokenCount,
        c.Response?.Usage?.CachedInputTokenCount,
        c.Response?.Latency.TotalMilliseconds,
        (int)c.HttpStatus,
        c.FinishReason,
        c.ErrorMessage,
        ComputeCost(c),
        c.CreatedAt,
        c.UpdatedAt,
        c.ConversationId,
        c.SessionId,
        (int)c.OutlierFlags);

    /// <summary>First user message in the request with collapsed whitespace; null when none/empty.
    /// Mirrors the frontend's old <c>firstUserMessage</c> so the list row reads a ready-made preview.</summary>
    private static string? FirstUserMessage(IAgentCall c)
    {
        var userMessage = c.Request.Messages.OfType<UserMessage>().FirstOrDefault();
        var text = userMessage?.GetText();
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static AgentCallMessageDto ToMessageDto(Message m) => m switch
    {
        AssistantMessage a => new AgentCallMessageDto(
            "assistant",
            a.GetText(),
            a.ToolRequests.Select(tr => new AgentCallToolRequestDto(tr.Id, tr.Name, tr.Arguments)).ToArray()),
        ToolMessage t => new AgentCallMessageDto("tool", t.GetText(), [], GetToolCallId(t)),
        _ => new AgentCallMessageDto(m.Role.ToString().ToLower(), m.GetText(), [])
    };

    private static string GetToolCallId(ToolMessage t)
        => t.Contents.Count > 0 ? t.Contents[0].Text ?? "" : "";

    private static decimal? ComputeCost(IAgentCall c)
        => c.Response?.Usage is { } usage ? c.Endpoint.CalculateCost(usage) : null;
}
