using Nordstein.Core.AI.Clients;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Application.TestCase;
using Proxytrace.Application.TestCase.Internal;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.AgentVersion;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Nordstein.Core.AI.Tools;
using Nordstein.Core.AI.Serialization;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.TestCase;

[TestClass]
public sealed class TestCaseSynthesisServiceTests : BaseTest<Module>
{
    private const string ValidResponse = """
        {
          "summary": "A customer asks for a refund and the agent grants it.",
          "proposals": [
            {
              "agentCallId": "REPLACE_ME",
              "kind": "Promotion",
              "title": "Looks up the order before refunding",
              "rationale": "The agent must check the order before acting.",
              "relevance": "High"
            }
          ],
          "skipped": [],
          "evaluatorSuggestion": null
        }
        """;

    [TestMethod]
    public async Task SynthesizeAsync_MapsTheModelsProposals()
    {
        IServiceProvider services = GetServices();
        var origin = FakeCall(out Guid callId);
        var service = BuildService(services, ValidResponse.Replace("REPLACE_ME", callId.ToString()), [origin]);

        var result = await service.SynthesizeAsync(origin, null, [], null, CancellationToken);

        result.Proposals.Should().ContainSingle();
        result.Proposals[0].AgentCallId.Should().Be(callId);
        result.Proposals[0].Kind.Should().Be(ProposalKind.Promotion);
        result.Proposals[0].Relevance.Should().Be(ProposalRelevance.High);
        result.Summary.Should().Contain("refund");
    }

    [TestMethod]
    public async Task SynthesizeAsync_WhenTheModelReturnsUnparseableJson_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var origin = FakeCall(out _);
        var service = BuildService(services, "not json at all", [origin]);

        var result = await service.SynthesizeAsync(origin, null, [], null, CancellationToken);

        result.Proposals.Should().BeEmpty();
        result.Summary.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SynthesizeAsync_SendsPriorRoundsAsAnAlternatingConversation()
    {
        IServiceProvider services = GetServices();
        var origin = FakeCall(out Guid callId);
        List<Conversation> recorded = [];
        var service = BuildService(
            services,
            RecordingAgent(services, ValidResponse.Replace("REPLACE_ME", callId.ToString()), recorded),
            [origin]);
        var round = new SynthesisRound(null, TestCaseProposalSet.Empty);

        await service.SynthesizeAsync(
            origin, null, [round], "test that issue_refund gets order_id=91", CancellationToken);

        Conversation sent = recorded.Should().ContainSingle().Subject;
        sent.Messages.Should().HaveCountGreaterThan(2);
        sent.Messages[0].Role.Should().Be(Role.User);
        sent.Messages[1].Role.Should().Be(Role.Assistant);
        sent.Messages[^1].Role.Should().Be(Role.User);
        sent.Messages[^1].GetText().Should().Contain("issue_refund");
    }

    [TestMethod]
    public async Task SynthesizeAsync_CapsThePriorRoundsItSends()
    {
        IServiceProvider services = GetServices();
        var origin = FakeCall(out Guid callId);
        List<Conversation> recorded = [];
        var service = BuildService(
            services,
            RecordingAgent(services, ValidResponse.Replace("REPLACE_ME", callId.ToString()), recorded),
            [origin]);
        SynthesisRound[] rounds =
            [.. Enumerable.Range(0, 12).Select(i => new SynthesisRound($"round {i}", TestCaseProposalSet.Empty))];

        await service.SynthesizeAsync(origin, null, rounds, "final", CancellationToken);

        Conversation sent = recorded.Should().ContainSingle().Subject;
        // 1 task turn + (instruction + assistant) per kept round + 1 final instruction.
        sent.Messages.Should().HaveCount(1 + (TestCaseProposalSet.MaxRounds * 2) + 1);
        sent.Messages.Should().NotContain(message => message.GetText().Contains("round 0"));
    }

    [TestMethod]
    public async Task SynthesizeAsync_PutsTheDestinationSuitesEvaluatorsInThePrompt()
    {
        IServiceProvider services = GetServices();
        var origin = FakeCall(out Guid callId);
        List<Conversation> recorded = [];
        var service = BuildService(
            services,
            RecordingAgent(services, ValidResponse.Replace("REPLACE_ME", callId.ToString()), recorded),
            [origin]);

        await service.SynthesizeAsync(origin, FakeSuite("Refund suite", "Exact Match"), [], null, CancellationToken);

        Conversation sent = recorded.Should().ContainSingle().Subject;
        sent.Messages[0].GetText().Should().Contain("Refund suite").And.Contain("Exact Match");
    }

    [TestMethod]
    public async Task SynthesizeAsync_AsksTheModelForNoReasoning()
    {
        // The user watches a panel block on this single call, and on a reasoning model the hidden
        // thinking dwarfs the answer — measured at 1.7k-3.0k reasoning tokens for a ~300-token JSON
        // answer, which is 25-44s of staring instead of 8-13s. Passing no options at all is what
        // made that the default.
        IServiceProvider services = GetServices();
        var origin = FakeCall(out Guid callId);
        List<Conversation> recorded = [];
        List<ModelOptions?> recordedOptions = [];
        var service = BuildService(
            services,
            RecordingAgent(services, ValidResponse.Replace("REPLACE_ME", callId.ToString()), recorded, recordedOptions),
            [origin]);

        await service.SynthesizeAsync(origin, null, [], null, CancellationToken);

        recordedOptions.Should().ContainSingle();
        recordedOptions[0]?.Sampling?.ReasoningEffort.Should().Be("none");
    }

    [TestMethod]
    public void Service_IsRegisteredInTheContainer()
    {
        // Guards the one line in Application/Module.cs that loads TestCaseModule — without it the
        // service is unreachable from the API and every test above still passes, because they
        // construct it by hand.
        IServiceProvider services = GetServices();

        services.GetRequiredService<ITestCaseSynthesisService>().Should().NotBeNull();
    }

    [TestMethod]
    public async Task PromptName_ResolvesFromTheEmbeddedResources()
    {
        // The other tests substitute IPromptTemplateRepository, so nothing else here would notice a
        // missing resx entry — which compiles fine and throws PromptNotFoundException at run time.
        IServiceProvider services = GetServices();
        var prompts = services.GetRequiredService<IPromptTemplateRepository>();

        var template = await prompts.FindAsync(TestCaseSynthesisService.PromptName, CancellationToken);

        template.Should().NotBeNull();
        template.Template.Should().Contain("CONSEQUENTIAL DECISION POINTS");
        // A `{{var}}` in the prompt would become a required template variable and throw at call
        // time, because nothing supplies one.
        template.Variables.Should().BeEmpty();
    }

    private static ITestCaseSynthesisService BuildService(
        IServiceProvider services,
        string cannedResponse,
        IReadOnlyList<IAgentCall> conversation)
        => BuildService(
            services,
            new CannedJsonAgent(cannedResponse, services.GetRequiredService<IOutputFormat.Create>()),
            conversation);

    private static ITestCaseSynthesisService BuildService(
        IServiceProvider services,
        IAgent systemAgent,
        IReadOnlyList<IAgentCall> conversation)
    {
        var prompts = Substitute.For<IPromptTemplateRepository>();
        var template = Substitute.For<IPromptTemplate>();
        template.Template.Returns("synthesize");
        prompts.GetAsync(TestCaseSynthesisService.PromptName, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(template));

        var agents = Substitute.For<IAgentRepository>();
        agents.GetOrCreateAsync(
                Arg.Any<IPromptTemplate>(), Arg.Any<IReadOnlyList<ToolSpecification>>(), Arg.Any<IProject>(),
                Arg.Any<IModelEndpoint>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<IModelParameters?>(),
                Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(systemAgent));

        var calls = Substitute.For<IAgentCallRepository>();
        calls.GetFilteredAsync(
                Arg.Any<AgentCallFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((conversation, conversation.Count)));

        return new TestCaseSynthesisService(calls, prompts, agents);
    }

    private static IAgentCall FakeCall(out Guid callId)
    {
        callId = Guid.NewGuid();

        var endpoint = Substitute.For<IModelEndpoint>();
        var project = Substitute.For<IProject>();
        project.Id.Returns(Guid.NewGuid());
        project.SystemEndpoint.Returns(endpoint);

        var prompt = Substitute.For<IPromptTemplate>();
        prompt.Template.Returns("You are a support agent.");

        var version = Substitute.For<IAgentVersion>();
        version.SystemPrompt.Returns(prompt);
        version.Tools.Returns(new List<ToolSpecification>
        {
            new("issue_refund", "Refund an order.", ToolArguments.None),
        });

        var agent = Substitute.For<IAgent>();
        agent.Name.Returns("Support bot");
        agent.Project.Returns(project);

        var completion = Substitute.For<ICompletion>();
        completion.Response.Returns(new AssistantMessage([Content.FromText("done")], []));

        var call = Substitute.For<IAgentCall>();
        call.Id.Returns(callId);
        call.ConversationId.Returns(Guid.NewGuid());
        call.Agent.Returns(agent);
        call.Version.Returns(version);
        call.Request.Returns(Conversation.Create().With(Message.CreateUserMessage("refund pls")));
        call.Response.Returns(completion);
        return call;
    }

    private static Domain.TestSuite.ITestSuite FakeSuite(string name, string evaluatorName)
    {
        var evaluator = Substitute.For<Domain.Evaluator.IEvaluator>();
        evaluator.Name.Returns(evaluatorName);
        evaluator.Kind.Returns(Domain.Evaluator.EvaluatorKind.ExactMatch);

        var suite = Substitute.For<Domain.TestSuite.ITestSuite>();
        suite.Name.Returns(name);
        suite.Evaluators.Returns(new List<Domain.Evaluator.IEvaluator> { evaluator });
        suite.TestCases.Returns(new List<Domain.TestCase.ITestCase>());
        return suite;
    }

    /// <summary>
    /// An agent whose client answers with the canned JSON and records the conversation it was asked
    /// to complete — and, when asked, the options it was asked with — so a test can assert what
    /// actually went to the model. Substituted rather than hand-written: implementing IAgent by
    /// delegation would be twenty pointless forwarding members.
    /// </summary>
    private static IAgent RecordingAgent(
        IServiceProvider services,
        string cannedResponse,
        List<Conversation> recorded,
        List<ModelOptions?>? recordedOptions = null)
    {
        var canned = new CannedJsonAgent(cannedResponse, services.GetRequiredService<IOutputFormat.Create>());

        var client = Substitute.For<IModelClient>();
        client.CompleteAsync<SynthesisOutput>(
                Arg.Any<Conversation>(),
                Arg.Any<ModelOptions?>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                // NSubstitute 6 hands back a nullable arg and `!` is banned repo-wide, so narrow it.
                Conversation? conversation = callInfo.Arg<Conversation>();
                ArgumentNullException.ThrowIfNull(conversation);
                recorded.Add(conversation);
                recordedOptions?.Add(callInfo.Arg<ModelOptions?>());
                using IModelClient inner = canned.CreateClient();
                return inner.CompleteAsync<SynthesisOutput>(conversation);
            });

        var agent = Substitute.For<IAgent>();
        agent.CreateClient(Arg.Any<IModelEndpoint?>(), Arg.Any<bool>()).Returns(client);
        return agent;
    }
}
