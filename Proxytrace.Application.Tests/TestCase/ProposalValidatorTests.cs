using AwesomeAssertions;
using NSubstitute;
using Proxytrace.Application.TestCase;
using Proxytrace.Application.TestCase.Internal;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.Tools;

namespace Proxytrace.Application.Tests.TestCase;

[TestClass]
public sealed class ProposalValidatorTests
{
    [TestMethod]
    public void Validate_DropsAProposalWhoseCallIsNotInTheConversation()
    {
        var call = FakeCall();
        var output = Output(Proposal(Guid.NewGuid()));       // an id from nowhere

        ProposalValidator.Validate(output, [call]).Proposals.Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_DropsAProposalWithAnUnparseableId()
    {
        var call = FakeCall();
        var output = Output(Proposal(call.Id) with { AgentCallId = "not-a-guid" });

        ProposalValidator.Validate(output, [call]).Proposals.Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_DropsAProposalOnACallWithNoResponse()
    {
        var call = FakeCall(withResponse: false);

        ProposalValidator.Validate(Output(Proposal(call.Id)), [call]).Proposals.Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_DropsACorrectionWithNoExpectedOutput()
    {
        var call = FakeCall();
        var output = Output(Proposal(call.Id) with { Kind = ProposalKind.Correction });

        ProposalValidator.Validate(output, [call]).Proposals.Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_FlagsACorrectionOnACallWhoseInputHasResolvedToolCalls()
    {
        var call = FakeCall(resolvedToolCalls: true);
        var output = Output(Proposal(call.Id) with
        {
            Kind = ProposalKind.Correction,
            ExpectedContent = "I cannot refund an order older than 30 days.",
        });

        var result = ProposalValidator.Validate(output, [call]);

        result.Proposals.Should().ContainSingle()
            .Which.Flags.Should().Contain(ProposalFlag.Unpassable);
    }

    [TestMethod]
    public void Validate_DoesNotFlagAPromotionOnACallWithResolvedToolCalls()
    {
        // A promotion asserts the response the agent actually gave, which agrees with its own input
        // by construction — the unpassable trap is specific to corrections.
        var call = FakeCall(resolvedToolCalls: true);

        var result = ProposalValidator.Validate(Output(Proposal(call.Id)), [call]);

        result.Proposals.Should().ContainSingle().Which.Flags.Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_FlagsAnExpectedToolTheAgentWasNotOffered()
    {
        var call = FakeCall();
        var output = Output(Proposal(call.Id) with
        {
            Kind = ProposalKind.Correction,
            ExpectedToolRequests = [new SynthesisToolRequest { Name = "delete_everything", Arguments = "{}" }],
        });

        var result = ProposalValidator.Validate(output, [call]);

        result.Proposals.Should().ContainSingle().Which.Flags.Should().Contain(ProposalFlag.UnknownTool);
    }

    [TestMethod]
    public void Validate_DoesNotFlagAnExpectedToolTheAgentWasOffered()
    {
        var call = FakeCall();
        var output = Output(Proposal(call.Id) with
        {
            Kind = ProposalKind.Correction,
            ExpectedToolRequests = [new SynthesisToolRequest { Name = "issue_refund", Arguments = """{"id":"91"}""" }],
        });

        var result = ProposalValidator.Validate(output, [call]);

        result.Proposals.Should().ContainSingle().Which.Flags.Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_CollapsesDuplicateCallAndKindPairs()
    {
        var call = FakeCall();
        var output = Output(Proposal(call.Id), Proposal(call.Id));

        ProposalValidator.Validate(output, [call]).Proposals.Should().ContainSingle();
    }

    [TestMethod]
    public void Validate_KeepsBothKindsForTheSameCall()
    {
        var call = FakeCall();
        var output = Output(
            Proposal(call.Id),
            Proposal(call.Id) with { Kind = ProposalKind.Correction, ExpectedContent = "better" });

        ProposalValidator.Validate(output, [call]).Proposals.Should().HaveCount(2);
    }

    [TestMethod]
    public void Validate_CapsProposalsAtTheMaximumKeepingTheMostRelevant()
    {
        // 12 distinct calls so nothing is deduped, of which exactly MaxProposals are High — so a
        // correct cap keeps every High one and drops both Low ones.
        List<IAgentCall> conversation = [];
        List<SynthesisProposal> proposals = [];
        for (int i = 0; i < 12; i++)
        {
            var member = FakeCall();
            conversation.Add(member);
            proposals.Add(Proposal(member.Id) with
            {
                Relevance = i < 2 ? ProposalRelevance.Low : ProposalRelevance.High,
            });
        }

        var result = ProposalValidator.Validate(Output([.. proposals]), conversation);

        result.Proposals.Should().HaveCount(TestCaseProposalSet.MaxProposals);
        result.Proposals.Should().OnlyContain(proposal => proposal.Relevance == ProposalRelevance.High);
    }

    [TestMethod]
    public void Validate_KeepsSkippedTurnsThatNameARealCall()
    {
        var call = FakeCall();
        var output = Output(Proposal(call.Id)) with
        {
            Skipped = [new SynthesisSkipped { AgentCallId = call.Id.ToString(), Reason = "closing summary" }],
        };

        ProposalValidator.Validate(output, [call]).Skipped.Should().ContainSingle()
            .Which.Reason.Should().Be("closing summary");
    }

    [TestMethod]
    public void Validate_DropsSkippedTurnsWithAnUnknownId()
    {
        var call = FakeCall();
        var output = Output(Proposal(call.Id)) with
        {
            Skipped = [new SynthesisSkipped { AgentCallId = Guid.NewGuid().ToString(), Reason = "?" }],
        };

        ProposalValidator.Validate(output, [call]).Skipped.Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_CarriesTheEvaluatorSuggestionThrough()
    {
        var call = FakeCall();
        var output = Output(Proposal(call.Id)) with
        {
            EvaluatorSuggestion = new SynthesisEvaluatorSuggestion
            {
                Name = "Refund policy judge",
                Instructions = "Decide whether the refusal cites the 30-day window.",
                Reason = "Exact Match cannot judge prose.",
                Target = EvaluatorSuggestionTarget.NewSuite,
            },
        };

        var suggestion = ProposalValidator.Validate(output, [call]).EvaluatorSuggestion;

        suggestion.Should().NotBeNull();
        suggestion.Target.Should().Be(EvaluatorSuggestionTarget.NewSuite);
    }

    [TestMethod]
    public void Validate_DropsAnEvaluatorSuggestionMissingItsInstructions()
    {
        var call = FakeCall();
        var output = Output(Proposal(call.Id)) with
        {
            EvaluatorSuggestion = new SynthesisEvaluatorSuggestion
            {
                Name = "Judge",
                Instructions = "   ",
                Reason = "because",
                Target = EvaluatorSuggestionTarget.Attach,
            },
        };

        ProposalValidator.Validate(output, [call]).EvaluatorSuggestion.Should().BeNull();
    }

    private static SynthesisOutput Output(params SynthesisProposal[] proposals)
        => new() { Summary = "summary", Proposals = proposals };

    private static SynthesisProposal Proposal(Guid callId)
        => new()
        {
            AgentCallId = callId.ToString(),
            Kind = ProposalKind.Promotion,
            Title = "title",
            Rationale = "rationale",
            Relevance = ProposalRelevance.High,
        };

    private static IAgentCall FakeCall(bool withResponse = true, bool resolvedToolCalls = false)
    {
        Conversation request = Conversation.Create().With(Message.CreateUserMessage("refund pls"));
        if (resolvedToolCalls)
        {
            var toolRequest = new ToolRequest("t1", "issue_refund", "{}");
            request = request
                .With(new AssistantMessage([], [toolRequest]))
                .With(Message.CreateToolMessage(new ToolResponse(toolRequest, [Content.FromText("ok")])));
        }

        var version = Substitute.For<IAgentVersion>();
        version.Tools.Returns(new List<ToolSpecification>
        {
            new("issue_refund", "Refund an order.", ToolArguments.None),
        });

        var call = Substitute.For<IAgentCall>();
        call.Id.Returns(Guid.NewGuid());
        call.Version.Returns(version);
        call.Request.Returns(request);
        if (withResponse)
        {
            var completion = Substitute.For<ICompletion>();
            completion.Response.Returns(new AssistantMessage([Content.FromText("done")], []));
            call.Response.Returns(completion);
        }
        else
        {
            // NSubstitute auto-substitutes interface-returning members, so an unconfigured
            // `Response` hands back a fake ICompletion rather than null — the "no response" case
            // has to be stated explicitly or it silently has one.
            call.Response.Returns((ICompletion?)null);
        }
        return call;
    }
}
