using System.Net;
using System.Text;
using System.Text.Json;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Application.Ingestion.Internal;

internal class OpenAiCallParser : IOpenAiCallParser
{
    private readonly ICompletion.Create completionFactory;
    private readonly IModelEndpointRepository endpointRepository;
    private readonly IModelParameters.Create modelParametersFactory;

    public OpenAiCallParser(
        ICompletion.Create completionFactory,
        IModelEndpointRepository endpointRepository,
        IModelParameters.Create modelParametersFactory)
    {
        this.completionFactory = completionFactory;
        this.endpointRepository = endpointRepository;
        this.modelParametersFactory = modelParametersFactory;
    }
    
    public async Task<ParseResult?> TryParse(IModelProvider provider,
        string requestBody,
        string? responseBody,
        TimeSpan duration,
        HttpStatusCode httpStatus,
        CancellationToken cancellationToken = default)
    {
        var conversation = ParseConversation(requestBody);
        if (conversation is null)
        {
            return null;
        }

        var agentMessage = ParseAgentMessage(responseBody);
        if (agentMessage is null && httpStatus is >= (HttpStatusCode)200 and < (HttpStatusCode)300)
        {
            return null;
        }

        var model = ParseModel(requestBody);
        var (inputTokens, outputTokens, cachedInputTokens, finishReason) = ParseUsage(responseBody);
        var usage = TokenUsage.Create(inputTokens ?? 0, outputTokens ?? 0, cachedInputTokens ?? 0);
        var errorMessage = (int)httpStatus >= 400 ? ParseErrorMessage(responseBody) : null;
        var systemMessage = ParseSystemMessage(requestBody);
        if(systemMessage is null)
        {
            return null;
        }
        
        var tools = ParseTools(requestBody);
        var modelParameters = ParseModelParameters(requestBody, modelParametersFactory);

        IModelEndpoint endpoint = await endpointRepository.GetOrCreateAsync(model, provider, cancellationToken);
        ICompletion? completion = agentMessage != null ? completionFactory(agentMessage, usage, duration) : null;
        return new ParseResult(
            Endpoint: endpoint,
            Request: conversation,
            Response: completion,
            HttpStatus: httpStatus,
            FinishReason: finishReason,
            ErrorMessage: errorMessage,
            SystemMessage: systemMessage,
            Tools: tools,
            ModelParameters: modelParameters);
    }

    private static IModelParameters ParseModelParameters(string requestBody, IModelParameters.Create factory)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestBody);
            var root = doc.RootElement;
            return factory(
                temperature: ReadDouble(root, "temperature"),
                topP: ReadDouble(root, "top_p"),
                reasoningEffort: ReadString(root, "reasoning_effort"),
                frequencyPenalty: ReadDouble(root, "frequency_penalty"),
                presencePenalty: ReadDouble(root, "presence_penalty"),
                maxTokens: ReadInt(root, "max_tokens") ?? ReadInt(root, "max_completion_tokens"),
                seed: ReadLong(root, "seed"),
                stop: ReadStop(root),
                n: ReadInt(root, "n"));
        }
        catch
        {
            return factory();
        }
    }

    private static double? ReadDouble(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        return el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var v) ? v : null;
    }

    private static int? ReadInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        return el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v) ? v : null;
    }

    private static long? ReadLong(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        return el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var v) ? v : null;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    private static IReadOnlyList<string>? ReadStop(JsonElement root)
    {
        if (!root.TryGetProperty("stop", out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.String)
        {
            return [el.GetString() ?? string.Empty];
        }

        if (el.ValueKind == JsonValueKind.Array)
        {
            return el.EnumerateArray()
                .Where(p => p.ValueKind == JsonValueKind.String)
                .Select(p => p.GetString() ?? string.Empty)
                .ToArray();
        }

        return null;
    }

    // ── OpenAI request → Conversation ────────────────────────────────────────

    private static Conversation? ParseConversation(string requestBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestBody);
            if (!doc.RootElement.TryGetProperty("messages", out var messagesEl))
            {
                return null;
            }

            var conversation = Conversation.Create();
            foreach (var msgEl in messagesEl.EnumerateArray())
            {
                var role = msgEl.TryGetProperty("role", out var rp) ? rp.GetString() : null;
                var message = ParseMessage(role, msgEl);
                if (message is null)
                {
                    return null;
                }

                conversation = message is SystemMessage sys
                    ? conversation.WithSystemMessage(sys)
                    : conversation.With(message);
            }
            return conversation;
        }
        catch { return null; }
    }

    private static Message? ParseMessage(string? role, JsonElement el) => role switch
    {
        // "developer" is the OpenAI role newer reasoning models use for the system prompt; the
        // AI SDK emits it in place of "system", so treat the two identically.
        "system"    => new SystemMessage(ParseContents(el)),
        "developer" => new SystemMessage(ParseContents(el)),
        "user"      => new UserMessage(ParseContents(el)),
        "assistant" => new AssistantMessage(ParseContents(el), ParseToolRequests(el)),
        "tool"      => ParseToolMessage(el),
        _           => null
    };

    private static IReadOnlyList<Content> ParseContents(JsonElement el)
    {
        if (!el.TryGetProperty("content", out var content))
        {
            return [];
        }

        if (content.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return [Content.FromText(content.GetString() ?? "")];
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            return content.EnumerateArray()
                .Where(p => p.TryGetProperty("type", out var t) && t.GetString() == "text"
                         && p.TryGetProperty("text", out _))
                .Select(p => Content.FromText(p.GetProperty("text").GetString() ?? ""))
                .ToArray();
        }

        return [];
    }

    private static IReadOnlyList<ToolRequest> ParseToolRequests(JsonElement el)
    {
        if (!el.TryGetProperty("tool_calls", out var toolCalls))
        {
            return [];
        }

        var result = new List<ToolRequest>();
        foreach (var tc in toolCalls.EnumerateArray())
        {
            var id = tc.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
            var name = "";
            var arguments = "{}";
            if (tc.TryGetProperty("function", out var fn))
            {
                if (fn.TryGetProperty("name", out var nameProp))
                {
                    name = nameProp.GetString() ?? "";
                }

                if (fn.TryGetProperty("arguments", out var argsProp))
                {
                    arguments = argsProp.GetString() ?? "{}";
                }
            }
            result.Add(new ToolRequest(id, name, arguments));
        }
        return result;
    }

    private static ToolMessage? ParseToolMessage(JsonElement el)
    {
        var id = el.TryGetProperty("tool_call_id", out var idProp) ? idProp.GetString() : null;
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        var contents = ParseContents(el);
        var resultContent = contents.Count switch
        {
            0 => Content.FromText("(no result)"),
            1 => contents[0],
            _ => Content.FromText(string.Concat(contents.Select(c => c.Text ?? "")))
        };

        return new ToolMessage(new ToolResponse(id, [resultContent], success: true, error: null));
    }

    // ── Agent identity extraction ─────────────────────────────────────────────

    private static SystemMessage? ParseSystemMessage(string requestBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestBody);
            if (!doc.RootElement.TryGetProperty("messages", out var messages))
            {
                return null;
            }

            foreach (var msg in messages.EnumerateArray())
            {
                // Accept "developer" as well as "system": reasoning models receive the system
                // prompt under the "developer" role (emitted by the AI SDK / OpenAI SDK).
                var role = msg.TryGetProperty("role", out var roleEl) ? roleEl.GetString() : null;
                if (role is not ("system" or "developer"))
                {
                    continue;
                }

                var contents = ParseContents(msg);
                if (contents.Count > 0)
                {
                    return new SystemMessage(contents);
                }
            }
        }
        catch { /* ignored */ }
        return null;
    }

    private static IReadOnlyList<ToolSpecification> ParseTools(string requestBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestBody);
            if (!doc.RootElement.TryGetProperty("tools", out var toolsEl)
                || toolsEl.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<ToolSpecification>();
            foreach (var tool in toolsEl.EnumerateArray())
            {
                // OpenAI format: { "type": "function", "function": { "name": "...", "description": "...", "parameters": {...} } }
                if (!tool.TryGetProperty("function", out var fn))
                {
                    continue;
                }

                var name = fn.TryGetProperty("name", out var n) ? n.GetString() : null;
                var description = fn.TryGetProperty("description", out var d) ? d.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
                {
                    continue;
                }

                var arguments = fn.TryGetProperty("parameters", out var p)
                    ? ToolArguments.FromJsonSchema(p)
                    : ToolArguments.None;

                result.Add(new ToolSpecification(name, description, arguments));
            }
            return result;
        }
        catch { return []; }
    }

    // ── OpenAI response → AssistantMessage ───────────────────────────────────

    private static AssistantMessage? ParseAgentMessage(string? responseBody)
    {
        if (responseBody is null)
        {
            return null;
        }

        // SSE streaming: lines are "data: <json>" or "data: [DONE]"
        if (LooksLikeSse(responseBody))
        {
            return ParseAgentMessageFromSse(responseBody);
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("choices", out var choices)
                || choices.GetArrayLength() == 0)
            {
                return null;
            }

            if (!choices[0].TryGetProperty("message", out var msg))
            {
                return null;
            }

            var role = msg.TryGetProperty("role", out var rp) ? rp.GetString() : null;
            return role != "assistant"
                ? null
                : new AssistantMessage(ParseContents(msg), ParseToolRequests(msg));
        }
        catch { return null; }
    }

    private static AssistantMessage? ParseAgentMessageFromSse(string responseBody)
    {
        var text = new StringBuilder();
        // Streamed tool calls arrive in fragments keyed by index: the first fragment carries the
        // id + name, later fragments only append argument text (no id). They must be reassembled
        // per index — parsing each chunk into its own ToolRequest yields fragments with an empty
        // id, which fail validation. Preserve first-seen order for stable output.
        var toolCalls = new Dictionary<int, StreamedToolCall>();
        var toolCallOrder = new List<int>();
        // Whether we saw at least one well-formed completion chunk. An assistant turn can legitimately
        // be empty (no text, no tool calls) — e.g. a "render-and-stop" step after a chart tool. That
        // is still a real captured call, and its request carries the preceding tool's result, so it
        // must be ingested rather than dropped. Only a body that yields no completion chunk at all is
        // not a parseable response.
        var sawCompletionChunk = false;

        foreach (var line in responseBody.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("data: ") || trimmed == "data: [DONE]")
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(trimmed["data: ".Length..]);
                if (!doc.RootElement.TryGetProperty("choices", out var choices)
                    || choices.GetArrayLength() == 0)
                {
                    continue;
                }

                sawCompletionChunk = true;
                var choice = choices[0];
                if (!choice.TryGetProperty("delta", out var delta))
                {
                    continue;
                }

                if (delta.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.String)
                {
                    text.Append(content.GetString());
                }

                AccumulateToolCallDeltas(delta, toolCalls, toolCallOrder);
            }
            catch { /* skip malformed chunk */ }
        }

        var contents = text.Length > 0
            ? (IReadOnlyList<Content>)[Content.FromText(text.ToString())]
            : [];

        var toolRequests = toolCallOrder
            .Select(index => toolCalls[index])
            // Drop any accumulator that never received an id/name (a stray fragment) so a partial
            // tool call can't produce an invalid ToolRequest.
            .Where(tc => !string.IsNullOrWhiteSpace(tc.Id) && !string.IsNullOrWhiteSpace(tc.Name))
            .Select(tc => new ToolRequest(
                tc.Id,
                tc.Name,
                tc.Arguments.Length > 0 ? tc.Arguments.ToString() : "{}"))
            .ToList();

        // Mirror the non-streaming path, which builds an AssistantMessage for any assistant response
        // including an empty one: keep the (possibly empty) message whenever a completion was
        // actually streamed, and return null only when the body was not a completion at all.
        return sawCompletionChunk
            ? new AssistantMessage(contents, toolRequests)
            : null;
    }

    /// <summary>
    /// Merges a streaming chat-completion delta's tool-call fragments into the per-index
    /// accumulators, keeping the id/name from whichever fragment carries them and concatenating
    /// argument text across fragments.
    /// </summary>
    private static void AccumulateToolCallDeltas(
        JsonElement delta,
        Dictionary<int, StreamedToolCall> toolCalls,
        List<int> order)
    {
        if (!delta.TryGetProperty("tool_calls", out var toolCallsEl)
            || toolCallsEl.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var tc in toolCallsEl.EnumerateArray())
        {
            var index = tc.TryGetProperty("index", out var idxEl) && idxEl.TryGetInt32(out var i) ? i : 0;
            if (!toolCalls.TryGetValue(index, out var acc))
            {
                acc = new StreamedToolCall();
                toolCalls[index] = acc;
                order.Add(index);
            }

            if (tc.TryGetProperty("id", out var idEl)
                && idEl.ValueKind == JsonValueKind.String
                && idEl.GetString() is { Length: > 0 } id)
            {
                acc.Id = id;
            }

            if (tc.TryGetProperty("function", out var fn))
            {
                if (fn.TryGetProperty("name", out var nameEl)
                    && nameEl.ValueKind == JsonValueKind.String
                    && nameEl.GetString() is { Length: > 0 } name)
                {
                    acc.Name = name;
                }

                if (fn.TryGetProperty("arguments", out var argsEl)
                    && argsEl.ValueKind == JsonValueKind.String)
                {
                    acc.Arguments.Append(argsEl.GetString());
                }
            }
        }
    }

    /// <summary>
    /// Mutable accumulator for a single streamed tool call, reassembled across SSE delta chunks.
    /// </summary>
    private sealed class StreamedToolCall
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public StringBuilder Arguments { get; } = new();
    }

    // ── Scalar field extraction ───────────────────────────────────────────────

    private static string ParseModel(string requestBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestBody);
            if (doc.RootElement.TryGetProperty("model", out var model))
            {
                return model.GetString() ?? "unknown";
            }
        }
        catch { /* ignored */ }
        return "unknown";
    }

    private static (ulong? inputTokens, ulong? outputTokens, ulong? cachedInputTokens, string? finishReason) ParseUsage(string? responseBody)
    {
        if (responseBody is null)
        {
            return (null, null, null, null);
        }

        if (LooksLikeSse(responseBody))
        {
            return ParseUsageFromSse(responseBody);
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            return ExtractUsageFromElement(doc.RootElement);
        }
        catch { return (null, null, null, null); }
    }

    /// <summary>
    /// Distinguishes an SSE-streamed body from a buffered JSON completion structurally. A
    /// streamed body is a sequence of <c>data: &lt;json&gt;</c> lines; a buffered body is a single
    /// JSON value. We must not use a naive <c>Contains("data: ")</c> — that substring legitimately
    /// occurs inside JSON string content (assistant text, tool arguments, error messages) and would
    /// misroute a normal completion into the SSE parser, losing its message and token usage.
    /// </summary>
    private static bool LooksLikeSse(string responseBody)
    {
        foreach (var line in responseBody.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0)
            {
                continue;
            }

            // First meaningful line decides: "data:" → SSE framing; "{"/"[" → buffered JSON value.
            if (trimmed.StartsWith("data:", StringComparison.Ordinal))
            {
                return true;
            }

            if (trimmed[0] is '{' or '[')
            {
                return false;
            }
        }

        return false;
    }

    private static (ulong? inputTokens, ulong? outputTokens, ulong? cachedInputTokens, string? finishReason) ParseUsageFromSse(string responseBody)
    {
        ulong? inputTokens = null;
        ulong? outputTokens = null;
        ulong? cachedInputTokens = null;
        string? finishReason = null;

        foreach (var line in responseBody.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("data: ") || trimmed == "data: [DONE]")
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(trimmed["data: ".Length..]);
                var (pt, ct, cached, fr) = ExtractUsageFromElement(doc.RootElement);
                inputTokens ??= pt;
                outputTokens ??= ct;
                cachedInputTokens ??= cached;
                finishReason ??= fr;
            }
            catch { /* skip malformed chunk */ }
        }

        return (inputTokens, outputTokens, cachedInputTokens, finishReason);
    }

    private static (ulong? inputTokens, ulong? outputTokens, ulong? cachedInputTokens, string? finishReason) ExtractUsageFromElement(JsonElement root)
    {
        ulong? inputTokens = null;
        ulong? outputTokens = null;
        ulong? cachedInputTokens = null;
        string? finishReason = null;

        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt) && pt.ValueKind == JsonValueKind.Number)
            {
                inputTokens = pt.GetUInt64();
            }

            if (usage.TryGetProperty("completion_tokens", out var ct) && ct.ValueKind == JsonValueKind.Number)
            {
                outputTokens = ct.GetUInt64();
            }

            cachedInputTokens = ExtractCachedInputTokens(usage);
        }

        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("finish_reason", out var fr) && fr.ValueKind != JsonValueKind.Null)
            {
                finishReason = fr.GetString();
            }
        }

        return (inputTokens, outputTokens, cachedInputTokens, finishReason);
    }

    /// <summary>
    /// Cached-input tokens served from the provider's prompt cache — a subset of the input tokens.
    /// Primary: OpenAI's <c>usage.prompt_tokens_details.cached_tokens</c>; fallback: an
    /// Anthropic-style top-level <c>usage.cache_read_input_tokens</c>.
    /// </summary>
    private static ulong? ExtractCachedInputTokens(JsonElement usage)
    {
        if (usage.TryGetProperty("prompt_tokens_details", out var details)
            && details.ValueKind == JsonValueKind.Object
            && details.TryGetProperty("cached_tokens", out var cached)
            && cached.ValueKind == JsonValueKind.Number)
        {
            return cached.GetUInt64();
        }

        if (usage.TryGetProperty("cache_read_input_tokens", out var anthropicCached)
            && anthropicCached.ValueKind == JsonValueKind.Number)
        {
            return anthropicCached.GetUInt64();
        }

        return null;
    }

    private static string? ParseErrorMessage(string? responseBody)
    {
        if (responseBody is null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var msg))
            {
                return msg.GetString();
            }
        }
        catch { /* ignored */ }
        return null;
    }
}
