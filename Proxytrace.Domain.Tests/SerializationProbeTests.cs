using Autofac;
using Nordstein.Core.Common.Serialization;
using Nordstein.Core.AI.Messages;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Domain.Tests;

[TestClass]
public class SerializationProbeTests
{
    private readonly ISerializer serializer;

    public SerializationProbeTests()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule<Module>();
        var container = builder.Build();
        serializer = container.Resolve<ISerializer>();
    }

    [TestMethod]
    public void Dump_ToolSpecification_Json()
    {
        var tool = new ToolSpecification(
            "lookup_order",
            "Look up an order by its ID and return its current status.",
            ToolArguments.FromJsonSchema("""
                {
                  "type": "object",
                  "properties": {
                    "order_id": {
                      "type": "string",
                      "description": "The order ID to look up"
                    }
                  },
                  "required": ["order_id"]
                }
                """));

        IReadOnlyCollection<ToolSpecification> tools = [tool];
        var json = serializer.Serialize(tools);
        Console.WriteLine("=== TOOLS JSON ===");
        Console.WriteLine(json);
        Assert.IsNotNull(json);
    }

    [TestMethod]
    public void Dump_SystemMessage_Json()
    {
        var msg = new SystemMessage("You are a helpful customer support agent.");
        var json = serializer.Serialize(msg);
        Console.WriteLine("=== SYSTEM MESSAGE JSON ===");
        Console.WriteLine(json);
        Assert.IsNotNull(json);
    }

    [TestMethod]
    public void Dump_Conversation_Json()
    {
        var toolReq = new ToolRequest("call_1", "lookup_order", """{"order_id":"12345"}""");
        var toolResp = new ToolResponse(toolReq, [Content.FromText("""{"status":"shipped","eta":"2026-04-27"}""")]);

        var conv = Conversation.Create()
            .With(new UserMessage([Content.FromText("What is the status of order #12345?")]))
            .With(new AssistantMessage([], [toolReq]))
            .With(new ToolMessage(toolResp))
            .With(new AssistantMessage([Content.FromText("Your order #12345 has shipped and arrives April 27.")], []));

        var json = serializer.Serialize(conv);
        Console.WriteLine("=== CONVERSATION JSON ===");
        Console.WriteLine(json);
        Assert.IsNotNull(json);
    }

    [TestMethod]
    public void Dump_AssistantMessage_Json()
    {
        var msg = new AssistantMessage([Content.FromText("Hello, how can I help you?")], []);
        var json = serializer.Serialize(msg);
        Console.WriteLine("=== ASSISTANT MESSAGE JSON ===");
        Console.WriteLine(json);
        Assert.IsNotNull(json);
    }
}
