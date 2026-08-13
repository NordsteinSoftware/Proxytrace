using Autofac;
using BenchmarkDotNet.Attributes;
using Nordstein.Core.Common.Serialization;
using Proxytrace.Domain.Message;

namespace Proxytrace.Benchmarks;

/// <summary>
/// Micro-benchmarks for the per-row JSON cost the storage layer pays on the hot paths: the EF value
/// converters serialize the request/response payloads on every ingestion write, and deserialize them
/// on every full-trace read. These are pure CPU (no DB) and bound the algorithmic cost that the
/// at-scale query/ingestion latencies build on top of.
/// </summary>
[MemoryDiagnoser]
public class SerializationBenchmarks
{
    private IContainer container = null!;
    private ISerializer serializer = null!;
    private Conversation conversation = null!;
    private string conversationJson = null!;
    private AssistantMessage response = null!;
    private string responseJson = null!;

    [GlobalSetup]
    public void Setup()
    {
        container = BenchFixture.Build();
        var samples = BenchFixture.CreateSamplesAsync(container).GetAwaiter().GetResult();
        serializer = samples.Serializer;
        conversation = samples.Conversation;
        conversationJson = samples.ConversationJson;
        response = samples.Response;
        responseJson = samples.ResponseJson;
    }

    [GlobalCleanup]
    public void Cleanup() => container.Dispose();

    [Benchmark]
    public string ConversationSerialize() => serializer.Serialize(conversation);

    [Benchmark]
    public Conversation ConversationDeserialize() => serializer.DeserializeRequired<Conversation>(conversationJson);

    [Benchmark]
    public string ResponseSerialize() => serializer.Serialize(response);

    [Benchmark]
    public AssistantMessage ResponseDeserialize() => serializer.DeserializeRequired<AssistantMessage>(responseJson);
}
