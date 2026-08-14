using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Application.Ingestion.Internal;
using Proxytrace.Domain;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelProvider;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests;

[TestClass]
public sealed class OpenAiCallParserTests : BaseTest<Module>
{
    private const string Model = "gpt-4o";

    private const string RequestBody = $$"""
                                         {
                                             "model": "{{Model}}",
                                             "messages": [
                                                 {"role": "system", "content": "You are Tracey."},
                                                 {"role": "user", "content": "Show me the dashboard."}
                                             ]
                                         }
                                         """;

    // OpenAI streams a tool call across many SSE chunks: the id + name arrive only in the first
    // delta, later deltas carry argument fragments with no id. They must be reassembled by index
    // into a single ToolRequest — otherwise each fragment becomes a ToolRequest with an empty Id.
    private const string StreamedToolCallResponse =
        "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"\"}}]}\n\n" +
        "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_abc\",\"type\":\"function\",\"function\":{\"name\":\"get_dashboard_stats\",\"arguments\":\"\"}}]}}]}\n\n" +
        "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"projectId\\\":\"}}]}}]}\n\n" +
        "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"p1\\\"}\"}}]}}]}\n\n" +
        "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"tool_calls\"}]}\n\n" +
        "data: [DONE]\n\n";

    [TestMethod]
    public async Task TryParse_StreamedToolCallSplitAcrossChunks_ReassemblesIntoOneToolRequest()
    {
        IServiceProvider services = GetServices();
        var parser = services.GetRequiredService<IOpenAiCallParser>();
        var providerGenerator = services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>();
        var provider = await providerGenerator.GetOrCreateAsync(CancellationToken);

        ParseResult? result = await parser.TryParse(
            provider,
            RequestBody,
            StreamedToolCallResponse,
            TimeSpan.FromMilliseconds(50),
            HttpStatusCode.OK,
            CancellationToken);

        result.Should().NotBeNull();
        ParseResult parsed = result ?? throw new InvalidOperationException("Parse returned null");
        parsed.Response.Should().NotBeNull();
        ICompletion completion = parsed.Response ?? throw new InvalidOperationException("No completion");

        IReadOnlyList<ToolRequest> toolRequests = completion.Response.ToolRequests;
        toolRequests.Should().HaveCount(1);
        toolRequests[0].Id.Should().Be("call_abc");
        toolRequests[0].Name.Should().Be("get_dashboard_stats");
        toolRequests[0].Arguments.Should().Be("{\"projectId\":\"p1\"}");
    }

    // Two parallel tool calls stream interleaved, distinguished only by their `index`. Each must
    // reassemble independently and in first-seen order.
    private const string TwoStreamedToolCallsResponse =
        "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_a\",\"function\":{\"name\":\"list_agents\",\"arguments\":\"\"}}]}}]}\n\n" +
        "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":1,\"id\":\"call_b\",\"function\":{\"name\":\"list_runs\",\"arguments\":\"\"}}]}}]}\n\n" +
        "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"a\\\":1}\"}}]}}]}\n\n" +
        "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":1,\"function\":{\"arguments\":\"{\\\"b\\\":2}\"}}]}}]}\n\n" +
        "data: [DONE]\n\n";

    // A request whose final message is a tool result. The only place that result is captured is
    // this (terminal) call's request — so if the call is dropped, the tool response is lost.
    private const string RequestWithTrailingToolResult = """
                                                          {
                                                              "model": "gpt-4o",
                                                              "messages": [
                                                                  {"role": "system", "content": "You are Tracey."},
                                                                  {"role": "user", "content": "Plot a chart."},
                                                                  {"role": "assistant", "content": "", "tool_calls": [{"id": "call_chart", "type": "function", "function": {"name": "show_chart", "arguments": "{}"}}]},
                                                                  {"role": "tool", "tool_call_id": "call_chart", "content": "{\"shown\":true}"}
                                                              ]
                                                          }
                                                          """;

    // The model's terminal step after a render tool: an assistant completion with no text and no
    // tool calls (it considers the rendered component the answer). Streamed as SSE.
    private const string EmptyStreamedCompletion =
        "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"\"}}]}\n\n" +
        "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n" +
        "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":6230,\"completion_tokens\":0,\"total_tokens\":6230}}\n\n" +
        "data: [DONE]\n\n";

    [TestMethod]
    public async Task TryParse_EmptyStreamedCompletion_IngestsTheCallSoItsToolResultIsCaptured()
    {
        IServiceProvider services = GetServices();
        var parser = services.GetRequiredService<IOpenAiCallParser>();
        var providerGenerator = services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>();
        var provider = await providerGenerator.GetOrCreateAsync(CancellationToken);

        ParseResult? result = await parser.TryParse(
            provider, RequestWithTrailingToolResult, EmptyStreamedCompletion,
            TimeSpan.FromMilliseconds(50), HttpStatusCode.OK, CancellationToken);

        // The call must be ingested (not dropped): an empty completion is still a real LLM call,
        // and its request is the only carrier of the preceding tool's result.
        ParseResult parsed = result ?? throw new InvalidOperationException("empty completion was dropped");
        parsed.Response.Should().NotBeNull();
        parsed.Request.Messages.Should().Contain(m => m is ToolMessage);
    }

    [TestMethod]
    public async Task TryParse_TwoStreamedToolCalls_ReassemblesEachByIndexInOrder()
    {
        IServiceProvider services = GetServices();
        var parser = services.GetRequiredService<IOpenAiCallParser>();
        var providerGenerator = services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>();
        var provider = await providerGenerator.GetOrCreateAsync(CancellationToken);

        ParseResult? result = await parser.TryParse(
            provider, RequestBody, TwoStreamedToolCallsResponse,
            TimeSpan.FromMilliseconds(50), HttpStatusCode.OK, CancellationToken);

        ParseResult parsed = result ?? throw new InvalidOperationException("Parse returned null");
        ICompletion completion = parsed.Response ?? throw new InvalidOperationException("No completion");

        IReadOnlyList<ToolRequest> toolRequests = completion.Response.ToolRequests;
        toolRequests.Should().HaveCount(2);
        toolRequests[0].Id.Should().Be("call_a");
        toolRequests[0].Name.Should().Be("list_agents");
        toolRequests[0].Arguments.Should().Be("{\"a\":1}");
        toolRequests[1].Id.Should().Be("call_b");
        toolRequests[1].Name.Should().Be("list_runs");
        toolRequests[1].Arguments.Should().Be("{\"b\":2}");
    }

    private const string BufferedResponseWithCachedTokens = """
        {
            "choices": [{"index": 0, "message": {"role": "assistant", "content": "Hi"}, "finish_reason": "stop"}],
            "usage": {"prompt_tokens": 1000, "completion_tokens": 50, "total_tokens": 1050, "prompt_tokens_details": {"cached_tokens": 800}}
        }
        """;

    [TestMethod]
    public async Task TryParse_BufferedResponseWithCachedTokens_CapturesCachedInputSubset()
    {
        IServiceProvider services = GetServices();
        var parser = services.GetRequiredService<IOpenAiCallParser>();
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().GetOrCreateAsync(CancellationToken);

        ParseResult? result = await parser.TryParse(
            provider, RequestBody, BufferedResponseWithCachedTokens,
            TimeSpan.FromMilliseconds(50), HttpStatusCode.OK, CancellationToken);

        ICompletion completion = result?.Response ?? throw new InvalidOperationException("No completion");
        completion.Usage.Should().NotBeNull();
        completion.Usage!.InputTokenCount.Should().Be(1000);
        completion.Usage.OutputTokenCount.Should().Be(50);
        completion.Usage.CachedInputTokenCount.Should().Be(800);
    }

    private const string BufferedResponseAnthropicCached = """
        {
            "choices": [{"index": 0, "message": {"role": "assistant", "content": "Hi"}, "finish_reason": "stop"}],
            "usage": {"prompt_tokens": 1000, "completion_tokens": 50, "cache_read_input_tokens": 600}
        }
        """;

    [TestMethod]
    public async Task TryParse_AnthropicStyleCacheReadField_IsCapturedAsCachedInput()
    {
        IServiceProvider services = GetServices();
        var parser = services.GetRequiredService<IOpenAiCallParser>();
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().GetOrCreateAsync(CancellationToken);

        ParseResult? result = await parser.TryParse(
            provider, RequestBody, BufferedResponseAnthropicCached,
            TimeSpan.FromMilliseconds(50), HttpStatusCode.OK, CancellationToken);

        ICompletion completion = result?.Response ?? throw new InvalidOperationException("No completion");
        completion.Usage!.CachedInputTokenCount.Should().Be(600);
    }

    [TestMethod]
    public async Task TryParse_UsageWithoutCachedDetails_ReportsZeroCached()
    {
        IServiceProvider services = GetServices();
        var parser = services.GetRequiredService<IOpenAiCallParser>();
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().GetOrCreateAsync(CancellationToken);

        // EmptyStreamedCompletion's usage carries prompt_tokens but no cached details.
        ParseResult? result = await parser.TryParse(
            provider, RequestWithTrailingToolResult, EmptyStreamedCompletion,
            TimeSpan.FromMilliseconds(50), HttpStatusCode.OK, CancellationToken);

        ICompletion completion = result?.Response ?? throw new InvalidOperationException("No completion");
        completion.Usage!.InputTokenCount.Should().Be(6230);
        completion.Usage.CachedInputTokenCount.Should().Be(0);
    }
}
