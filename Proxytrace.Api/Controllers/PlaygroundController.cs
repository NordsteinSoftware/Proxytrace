using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Dto.Playground;
using Proxytrace.Api.Json;
using Proxytrace.Application.ErrorLog;
using Proxytrace.Application.Playground;
using Proxytrace.Application.Playground.Internal;
using Proxytrace.Domain.Agent;

namespace Proxytrace.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/playground")]
public class PlaygroundController : ControllerBase
{
    private readonly IPlaygroundService service;
    private readonly IAgentRepository agents;
    private readonly IProjectAccessGuard accessGuard;
    private readonly ILogger<PlaygroundController> logger;
    private readonly IWebHostEnvironment env;

    public PlaygroundController(
        IPlaygroundService service,
        IAgentRepository agents,
        IProjectAccessGuard accessGuard,
        ILogger<PlaygroundController> logger,
        IWebHostEnvironment env)
    {
        this.service = service;
        this.agents = agents;
        this.accessGuard = accessGuard;
        this.logger = logger;
        this.env = env;
    }

    /// <summary>
    /// Streams a single playground completion turn as SSE.
    /// Events: token, tool-request, done, error.
    /// </summary>
    [HttpPost("complete")]
    public async Task Complete(
        [FromBody] PlaygroundCompleteRequestDto request,
        CancellationToken cancellationToken)
    {
        // Authorize before opening the stream: a playground completion runs on the agent's endpoint,
        // spending the owning project's upstream provider credential. Resolve the agent's project and
        // hide it behind a 404 when the caller is not a member (no existence oracle).
        var projectId = await agents.GetProjectIdAsync(request.AgentId, cancellationToken);
        if (projectId is null || !await accessGuard.CanAccessProjectAsync(projectId.Value, cancellationToken))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        var domainRequest = ToDomain(request);

        try
        {
            await foreach (var evt in service.CompleteStreamAsync(domainRequest, cancellationToken))
            {
                var (name, payload) = evt switch
                {
                    TokenEvent t => ("token", (object)new { delta = t.Delta }),
                    ToolRequestEvent tr => ("tool-request", new { id = tr.Id, name = tr.Name, arguments = tr.Arguments }),
                    DoneEvent d => ("done", new
                    {
                        inputTokens = d.InputTokens,
                        outputTokens = d.OutputTokens,
                        cachedInputTokens = d.CachedInputTokens,
                        latencyMs = d.LatencyMs,
                        costEur = d.CostEur,
                        finishReason = d.FinishReason,
                    }),
                    ErrorEvent e => ("error", new { message = e.Message }),
                    _ => ("error", new { message = "unknown event" }),
                };

                var data = JsonSerializer.Serialize(payload, ApiJsonOptions.Sse);
                await Response.WriteAsync($"event: {name}\ndata: {data}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (NotImplementedException)
        {
            var data = JsonSerializer.Serialize(new { message = "Playground backend not implemented yet" }, ApiJsonOptions.Sse);
            await Response.WriteAsync($"event: error\ndata: {data}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Mirror ExceptionHandlingMiddleware: capture under an error id and only surface the raw
            // message in Development. Outside Development the message may carry SQL/schema/paths, so
            // emit a generic message (carrying the error id) — the SSE error frame shape is unchanged.
            var errorId = Guid.NewGuid();
            using (logger.BeginScope(new Dictionary<string, object> { [ErrorLogScope.ErrorIdKey] = errorId }))
            {
                logger.LogError(ex, "Unhandled playground exception");
            }

            var message = env.IsDevelopment()
                ? ex.Message
                : $"An unexpected error occurred. (Error ID: {errorId})";
            var data = JsonSerializer.Serialize(new { message }, ApiJsonOptions.Sse);
            await Response.WriteAsync($"event: error\ndata: {data}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    private static PlaygroundCompleteRequest ToDomain(PlaygroundCompleteRequestDto dto) => new(
        dto.AgentId,
        dto.EndpointId,
        dto.SystemPrompt,
        new PlaygroundModelParameters(
            dto.Parameters.Temperature,
            dto.Parameters.TopP,
            dto.Parameters.ReasoningEffort,
            dto.Parameters.FrequencyPenalty,
            dto.Parameters.PresencePenalty,
            dto.Parameters.MaxTokens,
            dto.Parameters.Seed,
            dto.Parameters.Stop),
        dto.Tools.Select(t => new PlaygroundToolSpecification(
            t.Name,
            t.Description,
            t.Arguments.Select(a => new PlaygroundToolArgument(a.Name, a.Description, a.Type, a.IsRequired)).ToArray()))
            .ToArray(),
        dto.Messages.Select(m => new PlaygroundMessage(
            m.Role,
            m.Content,
            m.ToolRequests.Select(tr => new PlaygroundToolRequest(tr.Id, tr.Name, tr.Arguments)).ToArray(),
            m.ToolCallId,
            m.ToolSucceeded,
            m.ToolError)).ToArray());
}
