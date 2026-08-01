using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Application.ErrorLog;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Tools;
using Proxytrace.Domain.Usage;

namespace Proxytrace.Application.Playground.Internal;

internal sealed class PlaygroundService : IPlaygroundService
{
    private readonly IRepository<IAgent> agentRepository;
    private readonly IRepository<IModelEndpoint> endpointRepository;
    private readonly ILogger<PlaygroundService> logger;
    private readonly bool isDevelopment;

    public PlaygroundService(
        IRepository<IAgent> agentRepository,
        IRepository<IModelEndpoint> endpointRepository,
        ILogger<PlaygroundService> logger,
        IHostEnvironment? environment = null)
    {
        this.agentRepository = agentRepository;
        this.endpointRepository = endpointRepository;
        this.logger = logger;
        this.isDevelopment = environment?.IsDevelopment() ?? false;
    }

    /// <summary>
    /// Captures an exception under a fresh error id and returns a client-safe message for the SSE
    /// <c>error</c> frame. Mirrors <c>ExceptionHandlingMiddleware</c>: the raw message may carry
    /// SQL/schema/paths, so outside Development only a generic message (carrying the error id for
    /// support correlation) is surfaced; the full detail is preserved in the captured error log.
    /// </summary>
    private string DescribeError(Exception exception)
    {
        var errorId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { [ErrorLogScope.ErrorIdKey] = errorId }))
        {
            logger.LogError(exception, "Unhandled playground exception");
        }

        return isDevelopment
            ? exception.Message
            : $"An unexpected error occurred. (Error ID: {errorId})";
    }

    public async IAsyncEnumerable<PlaygroundEvent> CompleteStreamAsync(
        PlaygroundCompleteRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAgent? agent = null;
        IModelEndpoint? endpoint = null;
        string? setupError = null;
        try
        {
            agent = await agentRepository.GetAsync(request.AgentId, cancellationToken);
            endpoint = await endpointRepository.GetAsync(request.EndpointId, cancellationToken);
        }
        catch (Exception ex)
        {
            setupError = DescribeError(ex);
        }

        if (setupError != null || agent is null || endpoint is null)
        {
            yield return new ErrorEvent(setupError ?? "agent or endpoint not found");
            yield break;
        }

        SystemMessage systemMessage = new(request.SystemPrompt);
        IReadOnlyList<ToolSpecification> tools = ResolveTools(agent.Tools, request.Tools);
        // The parameters the user set in the right rail. They used to stop here: ModelOptions had
        // nowhere to put them, so every control silently did nothing.
        ModelOptions options = new(endpoint.Model.Name, tools, ToSampling(request.Parameters));
        Conversation conversation = BuildConversation(request.Messages);

        using IModelClient client = agent.CreateClient(endpoint, skipIngestion: true);

        ulong inputTokens = 0;
        ulong outputTokens = 0;
        ulong cachedInputTokens = 0;
        long latencyMs = 0;
        string? finishReason = null;
        string? streamError = null;

        IAsyncEnumerator<ModelStreamUpdate>? enumerator = null;
        try
        {
            enumerator = client.StreamAsync(systemMessage, conversation, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex)
        {
            streamError = DescribeError(ex);
        }

        if (streamError != null || enumerator is null)
        {
            yield return new ErrorEvent(streamError ?? "failed to start stream");
            yield break;
        }

        try
        {
            while (true)
            {
                ModelStreamUpdate? update = null;
                bool hasNext = false;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                    if (hasNext) update = enumerator.Current;
                }
                catch (Exception ex)
                {
                    streamError = DescribeError(ex);
                }

                if (streamError != null) break;
                if (!hasNext || update is null) break;

                switch (update)
                {
                    case TextDelta td:
                        yield return new TokenEvent(td.Text);
                        break;
                    case ToolRequested tr:
                        yield return new ToolRequestEvent(tr.Request.Id, tr.Request.Name, tr.Request.Arguments);
                        break;
                    case Completed done:
                        inputTokens = done.Usage?.InputTokenCount ?? 0;
                        outputTokens = done.Usage?.OutputTokenCount ?? 0;
                        cachedInputTokens = done.Usage?.CachedInputTokenCount ?? 0;
                        latencyMs = (long)done.Latency.TotalMilliseconds;
                        finishReason = done.FinishReason;
                        break;
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        if (streamError != null)
        {
            yield return new ErrorEvent(streamError);
            yield break;
        }

        decimal? cost = null;
        if (endpoint.InputTokenCost is not null && endpoint.OutputTokenCost is not null && (inputTokens > 0 || outputTokens > 0))
        {
            cost = endpoint.CalculateCost(new TokenUsage(inputTokens, outputTokens, cachedInputTokens));
        }

        yield return new DoneEvent(inputTokens, outputTokens, cachedInputTokens, latencyMs, cost, finishReason);
    }

    /// <summary>
    /// Apply override list onto agent's tools: keep only tools whose name appears in the override,
    /// substitute description and per-arg description from the override.
    /// Tools in the override that don't match an agent tool by name are skipped (no schema available).
    /// </summary>
    /// <summary>
    /// Maps the playground's parameter panel onto the request's sampling overrides.
    /// </summary>
    /// <remarks>
    /// A blank stop-sequence list is normalised to <see langword="null"/> so an untouched control
    /// sends nothing at all rather than an empty array the provider then has to interpret.
    /// <para>
    /// Choice count (<c>n</c>) is not mapped and is no longer offered: the playground streams into a
    /// single message, and the OpenAI stream carries no choice index, so more than one completion
    /// would arrive interleaved and unattributable while billing for all of them. See issue #496.
    /// </para>
    /// </remarks>
    private static ModelSamplingParameters ToSampling(PlaygroundModelParameters parameters)
        => new(
            Temperature: parameters.Temperature,
            TopP: parameters.TopP,
            FrequencyPenalty: parameters.FrequencyPenalty,
            PresencePenalty: parameters.PresencePenalty,
            MaxOutputTokens: parameters.MaxTokens,
            Seed: parameters.Seed,
            StopSequences: parameters.Stop is { Count: > 0 } stop ? stop : null,
            ReasoningEffort: string.IsNullOrWhiteSpace(parameters.ReasoningEffort) ? null : parameters.ReasoningEffort);

    private static IReadOnlyList<ToolSpecification> ResolveTools(
        IReadOnlyList<ToolSpecification> agentTools,
        IReadOnlyList<PlaygroundToolSpecification> overrides)
    {
        if (overrides.Count == 0) return [];

        var byName = agentTools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var result = new List<ToolSpecification>(overrides.Count);

        foreach (var ov in overrides)
        {
            if (!byName.TryGetValue(ov.Name, out var original)) continue;

            var argOverrides = ov.Arguments.ToDictionary(a => a.Name, StringComparer.Ordinal);
            var newArgs = original.Arguments.Arguments
                .Select(arg =>
                    argOverrides.TryGetValue(arg.Name, out var ao) && !string.Equals(ao.Description, arg.Description)
                        ? CloneArgWithDescription(arg, ao.Description)
                        : arg)
                .ToList();

            result.Add(new ToolSpecification(ov.Name, ov.Description, new ToolArguments(newArgs)));
        }

        return result;
    }

    private static IToolArgument CloneArgWithDescription(IToolArgument arg, string? description)
    {
        try
        {
            using var doc = JsonDocument.Parse(arg.JsonSchema);
            var dict = new Dictionary<string, object?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("description")) continue;
                dict[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
            }
            if (!string.IsNullOrWhiteSpace(description)) dict["description"] = description;
            var json = JsonSerializer.SerializeToElement(dict);
            return new OverriddenJsonArgument(arg.Name, arg.IsRequired, json);
        }
        catch
        {
            return arg;
        }
    }

    private sealed record OverriddenJsonArgument(string Name, bool IsRequired, JsonElement Json) : IToolArgument
    {
        public string? Description
            => Json.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;

        public Type Type => typeof(object);
        public object? DefaultValue => null;
        public string JsonSchema => Json.GetRawText();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            => [];
    }

    private static Conversation BuildConversation(IReadOnlyList<PlaygroundMessage> messages)
    {
        var converted = new List<Message>();
        foreach (var m in messages)
        {
            var role = m.Role.ToLowerInvariant();
            if (role == "system") continue; // system message provided separately

            switch (role)
            {
                case "user":
                    converted.Add(Message.CreateUserMessage(m.Content));
                    break;
                case "assistant":
                {
                    IReadOnlyList<Content> contents = string.IsNullOrEmpty(m.Content)
                        ? Array.Empty<Content>()
                        : new[] { Content.FromText(m.Content) };
                    var toolRequests = (m.ToolRequests)
                        .Select(tr => new ToolRequest(tr.Id, tr.Name, tr.Arguments))
                        .ToList();
                    converted.Add(Message.CreateAssistantMessage(contents, toolRequests));
                    break;
                }
                case "tool":
                {
                    var id = m.ToolCallId ?? string.Empty;
                    if (string.IsNullOrEmpty(id)) continue;
                    var request = new ToolRequest(id, "", "{}");
                    ToolResponse response = m.ToolSucceeded
                        ? new ToolResponse(request, [Content.FromText(m.Content)])
                        : new ToolResponse(request, new InvalidOperationException(m.ToolError ?? "tool error"));
                    converted.Add(Message.CreateToolMessage(response));
                    break;
                }
            }
        }
        return new Conversation(converted);
    }
}
