using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Clients;
using System.Text;
using System.Text.Json;
using JetBrains.Annotations;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelEndpoint;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Application.TestCase.Internal;

[UsedImplicitly]
internal sealed class TestCaseSynthesisService : ITestCaseSynthesisService
{
    internal const string PromptName = "test_case_synthesizer";

    /// <summary>
    /// Upper bound on the calls read for one conversation. A runaway client that reuses one
    /// conversation id for thousands of calls must not turn a review click into an unbounded read.
    /// </summary>
    private const int MaxConversationCalls = 100;

    /// <summary>
    /// The reasoning budget asked of the synthesis agent — none at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole interaction is one blocking model call the user watches a panel wait for, and on a
    /// reasoning model the thinking dwarfs the answer: measured against a four-call conversation on
    /// the shipped demo endpoint, a synthesis call spent 1.7k–3.0k tokens on hidden reasoning to
    /// produce a ~300-token JSON answer, taking 25–44s. Asking for none returned the same proposals
    /// in 8–13s.
    /// </para>
    /// <para>
    /// The task justifies it: this agent reads a transcript and reports the decision points in it,
    /// which is extraction rather than deduction, and nothing it says is trusted anyway —
    /// <see cref="ProposalValidator"/> re-checks every id against the real conversation. A model
    /// that has no such knob rejects the parameter and <c>ModelClient</c> re-asks without it, so
    /// this is a request for speed, never a requirement.
    /// </para>
    /// </remarks>
    private const string ReasoningBudget = "none";

    private static readonly JsonSerializerOptions RoundJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly IAgentCallRepository calls;
    private readonly IPromptTemplateRepository prompts;
    private readonly IAgentRepository agents;

    public TestCaseSynthesisService(
        IAgentCallRepository calls,
        IPromptTemplateRepository prompts,
        IAgentRepository agents)
    {
        this.calls = calls;
        this.prompts = prompts;
        this.agents = agents;
    }

    public async Task<TestCaseProposalSet> SynthesizeAsync(
        IAgentCall origin,
        ITestSuite? destination,
        IReadOnlyList<SynthesisRound> priorRounds,
        string? instruction,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IAgentCall> conversation = await LoadConversationAsync(origin, cancellationToken);
        SynthesisTranscript transcript = ConversationTranscript.Build(conversation);

        IPromptTemplate systemPrompt = await prompts.GetAsync(PromptName, cancellationToken);
        IAgent synthesizer = await agents.GetOrCreateAsync(
            systemPrompt: systemPrompt,
            tools: [],
            project: origin.Agent.Project,
            endpoint: origin.Agent.Project.SystemEndpoint,
            name: PromptName,
            isSystemAgent: true,
            cancellationToken: cancellationToken);

        Conversation modelConversation = BuildModelConversation(transcript, destination, priorRounds, instruction);

        using IModelClient client = synthesizer.CreateClient();
        ModelOptions options = ModelOptionsFactory.FromAgent(synthesizer, synthesizer.Endpoint.Model) with
        {
            Sampling = new ModelSamplingParameters(ReasoningEffort: ReasoningBudget),
        };
        SynthesisOutput? output = await CompleteWithRetryAsync(client, modelConversation, options, cancellationToken);

        // A null output means the model did not produce parseable JSON. An internal feature failing
        // must not take the caller down — report nothing rather than throwing.
        return output is null
            ? TestCaseProposalSet.Empty
            : ProposalValidator.Validate(output, conversation);
    }

    private static async Task<SynthesisOutput?> CompleteWithRetryAsync(
        IModelClient client,
        Conversation conversation,
        ModelOptions options,
        CancellationToken cancellationToken)
    {
        TypedCompletion<SynthesisOutput> completion =
            await client.CompleteAsync<SynthesisOutput>(conversation, options, cancellationToken: cancellationToken);
        if (completion.Response is not null)
        {
            return completion.Response;
        }

        // Sampling is non-deterministic, so the usual cause — a JSON answer cut off mid-string — does
        // not repeat. One retry costs a call; not retrying costs the user the whole interaction.
        completion = await client.CompleteAsync<SynthesisOutput>(conversation, options, cancellationToken: cancellationToken);
        return completion.Response;
    }

    private async Task<IReadOnlyList<IAgentCall>> LoadConversationAsync(
        IAgentCall origin,
        CancellationToken cancellationToken)
    {
        if (origin.ConversationId is not { } conversationId)
        {
            return [origin];
        }

        var (items, _) = await calls.GetFilteredAsync(
            new AgentCallFilter(
                ProjectId: origin.Agent.Project.Id,
                ConversationId: conversationId,
                SortBy: AgentCallSortField.CreatedAt,
                SortDescending: false),
            page: 1,
            pageSize: MaxConversationCalls,
            cancellationToken);

        return items.Count > 0 ? items : [origin];
    }

    /// <summary>
    /// Builds the model conversation: the task turn carrying the transcript, then one
    /// instruction/answer pair per prior round, then the new instruction. Refinement is a real
    /// conversation rather than a re-prompt, so the agent revises its own previous answer instead of
    /// starting over — and the rounds carry the proposals AS THE USER CURRENTLY HAS THEM, including
    /// expected outputs they edited in the panel.
    /// </summary>
    private static Conversation BuildModelConversation(
        SynthesisTranscript transcript,
        ITestSuite? destination,
        IReadOnlyList<SynthesisRound> priorRounds,
        string? instruction)
    {
        Conversation conversation = Conversation.Create()
            .With(Message.CreateUserMessage(BuildTask(transcript, destination)));

        foreach (SynthesisRound round in priorRounds.TakeLast(TestCaseProposalSet.MaxRounds))
        {
            if (!string.IsNullOrWhiteSpace(round.Instruction))
            {
                conversation = conversation.With(Message.CreateUserMessage(round.Instruction));
            }
            conversation = conversation.With(new AssistantMessage(
                [Content.FromText(JsonSerializer.Serialize(round.Proposals, RoundJson))],
                []));
        }

        return conversation.With(Message.CreateUserMessage(
            string.IsNullOrWhiteSpace(instruction)
                ? "Propose the test cases worth building from this conversation."
                : instruction));
    }

    private static string BuildTask(SynthesisTranscript transcript, ITestSuite? destination)
    {
        var builder = new StringBuilder();
        builder.AppendLine(transcript.Text);
        builder.AppendLine();

        if (destination is null)
        {
            builder.AppendLine("DESTINATION SUITE: none chosen yet.");
        }
        else
        {
            builder.AppendLine($"DESTINATION SUITE: {destination.Name}");
            builder.AppendLine(
                $"Its evaluators: {string.Join(", ", destination.Evaluators.Select(e => $"{e.Name} ({e.Kind})"))}");
            builder.AppendLine($"It already holds {destination.TestCases.Count} case(s).");
        }

        return builder.ToString();
    }
}
