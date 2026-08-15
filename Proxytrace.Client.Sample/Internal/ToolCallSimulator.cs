using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Proxytrace.Client.Sample.Internal;

/// <summary>
/// Demonstrates a multi-turn tool-call conversation routed through the Proxytrace proxy.
/// Proxytrace captures every leg: the initial request with tool specs, the assistant's tool_calls
/// response, the tool-result message, and the final answer — letting you inspect the full
/// decoded trace in the UI.
/// </summary>
internal class ToolCallSimulator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly Configuration configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolCallSimulator"/> class.
    /// </summary>
    public ToolCallSimulator(Configuration configuration)
    {
        this.configuration = configuration;
    }

    /// <summary>
    /// Runs.
    /// </summary>
    public async Task Run(CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient();
        httpClient.BaseAddress = new Uri(configuration.Endpoint.TrimEnd('/') + "/");
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", configuration.ApiKey);

        var tools = BuildTools();
        var messages = new List<object>
        {
            new { role = "system", content = "You are a helpful travel and weather assistant. Use the provided tools to answer questions." },
            new { role = "user",   content = "I'm planning a trip to Vienna next week. What's the weather like there, and can you recommend the top 3 tourist attractions?" },
        };

        // ── Turn 1: send user message + tool definitions ──────────────────────
        Console.WriteLine("  [tools] Turn 1 — sending user message with tool definitions...");
        var turn1Response = await PostChatCompletion(httpClient, messages, tools, cancellationToken);
        if (turn1Response is null)
        {
            return;
        }

        Console.WriteLine($"  [tools] Turn 1 — finish_reason: {turn1Response.FinishReason}");

        if (turn1Response.ToolCalls.Count == 0)
        {
            Console.WriteLine($"  [tools] Turn 1 — no tool calls in response (model answered directly): {Truncate(turn1Response.Content)}");
            return;
        }

        // Add assistant message with tool_calls to conversation
        messages.Add(new
        {
            role = "assistant",
            content = (string?)null,
            tool_calls = turn1Response.ToolCalls.Select(tc => new
            {
                id = tc.Id,
                type = "function",
                function = new { name = tc.Name, arguments = tc.Arguments },
            }).ToArray(),
        });

        // ── Turn 2: execute tools locally, feed results back ──────────────────
        foreach (var toolCall in turn1Response.ToolCalls)
        {
            Console.WriteLine($"  [tools] Tool call — {toolCall.Name}({toolCall.Arguments})");
            var result = ExecuteTool(toolCall.Name, toolCall.Arguments);
            Console.WriteLine($"  [tools] Tool result — {Truncate(result)}");

            messages.Add(new
            {
                role = "tool",
                tool_call_id = toolCall.Id,
                content = result,
            });
        }

        Console.WriteLine("  [tools] Turn 2 — sending tool results...");
        var turn2Response = await PostChatCompletion(httpClient, messages, tools: null, cancellationToken);
        if (turn2Response is null)
        {
            return;
        }

        Console.WriteLine($"  [tools] Turn 2 — finish_reason: {turn2Response.FinishReason}");
        Console.WriteLine($"  [tools] Final answer: {Truncate(turn2Response.Content, 200)}");
    }

    // ── Tool definitions ──────────────────────────────────────────────────────

    private static object[] BuildTools() =>
    [
        new
        {
            type = "function",
            function = new
            {
                name = "get_current_weather",
                description = "Returns current weather conditions for a given city.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        location = new { type = "string", description = "City name, e.g. Vienna, AT" },
                        unit    = new { type = "string", @enum = new[] { "celsius", "fahrenheit" }, description = "Temperature unit" },
                    },
                    required = new[] { "location" },
                },
            },
        },
        new
        {
            type = "function",
            function = new
            {
                name = "search_attractions",
                description = "Returns a list of top tourist attractions for a given city.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        city  = new { type = "string", description = "City to search attractions for" },
                        limit = new { type = "integer", description = "Maximum number of results (default 5)" },
                    },
                    required = new[] { "city" },
                },
            },
        },
    ];

    // ── Simulated tool execution ──────────────────────────────────────────────

    private static string ExecuteTool(string name, string argumentsJson)
    {
        try
        {
            var args = JsonNode.Parse(argumentsJson);
            return name switch
            {
                "get_current_weather" => GetCurrentWeather(args),
                "search_attractions"  => SearchAttractions(args),
                _                     => $"{{\"error\": \"Unknown tool: {name}\"}}",
            };
        }
        catch
        {
            return $"{{\"error\": \"Failed to execute tool {name}\"}}";
        }
    }

    private static string GetCurrentWeather(JsonNode? args)
    {
        var location = args?["location"]?.GetValue<string>() ?? "unknown";
        var unit = args?["unit"]?.GetValue<string>() ?? "celsius";
        var temp = unit == "fahrenheit" ? "54°F" : "12°C";
        return JsonSerializer.Serialize(new
        {
            location,
            temperature = temp,
            condition = "Partly cloudy",
            humidity = "65%",
            wind = "15 km/h NW",
        });
    }

    private static string SearchAttractions(JsonNode? args)
    {
        var city = args?["city"]?.GetValue<string>() ?? "unknown";
        var limit = args?["limit"]?.GetValue<int>() ?? 5;
        var attractions = new[]
        {
            new { name = "Schönbrunn Palace", rating = 4.8, category = "Historic Site" },
            new { name = "St. Stephen's Cathedral", rating = 4.7, category = "Religious Site" },
            new { name = "Belvedere Museum", rating = 4.7, category = "Museum" },
            new { name = "Vienna State Opera", rating = 4.6, category = "Performing Arts" },
            new { name = "Naschmarkt", rating = 4.5, category = "Market" },
        };
        return JsonSerializer.Serialize(new
        {
            city,
            attractions = attractions.Take(Math.Max(1, limit)).ToArray(),
        });
    }

    // ── HTTP helper ───────────────────────────────────────────────────────────

    private async Task<ChatResponse?> PostChatCompletion(
        HttpClient httpClient,
        IEnumerable<object> messages,
        object[]? tools,
        CancellationToken cancellationToken)
    {
        var payload = tools is { Length: > 0 }
            ? new { model = "gpt-4o-mini", messages, tools, max_tokens = 256 }
            : (object)new { model = "gpt-4o-mini", messages, max_tokens = 512 };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await httpClient.PostAsync("openai/v1/chat/completions", content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseResponse(body);
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"  [tools] Could not reach Proxytrace.Api ({ex.Message}). Start Proxytrace.Api to test this scenario.");
            return null;
        }
    }

    private static ChatResponse? ParseResponse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                return null;
            }

            var choice = choices[0];
            var finishReason = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;
            var msg = choice.TryGetProperty("message", out var m) ? m : (JsonElement?)null;

            var textContent = msg?.TryGetProperty("content", out var c) == true && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;

            var toolCalls = new List<ToolCallInfo>();
            if (msg?.TryGetProperty("tool_calls", out var tcs) == true && tcs.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in tcs.EnumerateArray())
                {
                    var id   = tc.TryGetProperty("id", out var idp)  ? idp.GetString() ?? "" : "";
                    var name = "";
                    var args = "{}";
                    if (tc.TryGetProperty("function", out var fn))
                    {
                        name = fn.TryGetProperty("name",      out var np) ? np.GetString() ?? "" : "";
                        args = fn.TryGetProperty("arguments", out var ap) ? ap.GetString() ?? "{}" : "{}";
                    }
                    toolCalls.Add(new ToolCallInfo(id, name, args));
                }
            }

            return new ChatResponse(textContent, toolCalls, finishReason);
        }
        catch { return null; }
    }

    private static string Truncate(string? s, int max = 120) =>
        s is null ? "(null)" : s.Length <= max ? s : s[..max] + "…";

    // ── Response models ───────────────────────────────────────────────────────

    private sealed record ChatResponse(string? Content, List<ToolCallInfo> ToolCalls, string? FinishReason);
    private sealed record ToolCallInfo(string Id, string Name, string Arguments);
}
