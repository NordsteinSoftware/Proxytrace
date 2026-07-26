using AwesomeAssertions;
using NSubstitute;
using Proxytrace.Domain.CostLimitBreach;
using Proxytrace.Proxy.Internal;

namespace Proxytrace.Proxy.Tests;

/// <summary>
/// Scope matching for monthly-budget hard blocks. A project-wide block always applies; an
/// agent-scoped one only when the request named that agent via <c>x-proxytrace-agent</c>.
/// </summary>
[TestClass]
public sealed class BudgetBlockerTests
{
    [TestMethod]
    public async Task Evaluate_ProjectScopedBlock_AppliesEvenWithoutAgentHeader()
    {
        var blocker = new BudgetBlocker(BlocksOf(ProjectBlock()));

        BudgetBlockMatch? match = await blocker.EvaluateAsync(Guid.NewGuid(), null, CancellationToken.None);

        // Unattributed traffic is exactly why project-level budgets are the reliable backstop.
        match.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(match);
        match.AgentName.Should().BeNull();
    }

    [TestMethod]
    public async Task Evaluate_AgentScopedBlock_WithoutAgentHeader_DoesNotApply()
    {
        var blocker = new BudgetBlocker(BlocksOf(AgentBlock("Support bot")));

        BudgetBlockMatch? match = await blocker.EvaluateAsync(Guid.NewGuid(), null, CancellationToken.None);

        // The header is the only pre-upstream attribution signal; without it the scope is unknown.
        match.Should().BeNull();
    }

    [TestMethod]
    public async Task Evaluate_AgentScopedBlock_WithMatchingHeader_Applies()
    {
        var blocker = new BudgetBlocker(BlocksOf(AgentBlock("Support bot")));

        BudgetBlockMatch? match = await blocker.EvaluateAsync(
            Guid.NewGuid(), "Support bot", CancellationToken.None);

        match.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(match);
        match.AgentName.Should().Be("Support bot");
    }

    [TestMethod]
    public async Task Evaluate_AgentScopedBlock_MatchesHeaderCaseInsensitively()
    {
        var blocker = new BudgetBlocker(BlocksOf(AgentBlock("Support Bot")));

        BudgetBlockMatch? match = await blocker.EvaluateAsync(
            Guid.NewGuid(), "support bot", CancellationToken.None);

        match.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Evaluate_AgentScopedBlock_WithDifferentAgentHeader_DoesNotApply()
    {
        var blocker = new BudgetBlocker(BlocksOf(AgentBlock("Support bot")));

        BudgetBlockMatch? match = await blocker.EvaluateAsync(
            Guid.NewGuid(), "Billing bot", CancellationToken.None);

        match.Should().BeNull();
    }

    [TestMethod]
    public async Task Evaluate_WithNoBlocks_Passes()
    {
        var blocker = new BudgetBlocker(BlocksOf());

        BudgetBlockMatch? match = await blocker.EvaluateAsync(
            Guid.NewGuid(), "Support bot", CancellationToken.None);

        match.Should().BeNull();
    }

    [TestMethod]
    public async Task Evaluate_MixedBlocks_ReturnsTheOneThatApplies()
    {
        BudgetHardBlock agentBlock = AgentBlock("Billing bot");
        var blocker = new BudgetBlocker(BlocksOf(agentBlock, ProjectBlock()));

        BudgetBlockMatch? match = await blocker.EvaluateAsync(
            Guid.NewGuid(), "Support bot", CancellationToken.None);

        // The agent-scoped block names a different agent, so the project-wide one is what bites.
        match.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(match);
        match.CostLimitId.Should().NotBe(agentBlock.CostLimitId);
    }

    private static BudgetHardBlock ProjectBlock()
        => new(Guid.NewGuid(), null, null);

    private static BudgetHardBlock AgentBlock(string agentName)
        => new(Guid.NewGuid(), Guid.NewGuid(), agentName);

    private static IBudgetBlockProvider BlocksOf(params BudgetHardBlock[] blocks)
    {
        var provider = Substitute.For<IBudgetBlockProvider>();
        provider.GetBlocksAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(blocks);
        return provider;
    }
}
