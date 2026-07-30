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

        BudgetBlockMatch? match = await blocker.EvaluateAsync(Guid.NewGuid(), null, null, CancellationToken.None);

        // Unattributed traffic is exactly why project-level budgets are the reliable backstop.
        match.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(match);
        match.AgentName.Should().BeNull();
    }

    [TestMethod]
    public async Task Evaluate_AgentScopedBlock_WithoutAgentHeader_DoesNotApply()
    {
        var blocker = new BudgetBlocker(BlocksOf(AgentBlock("Support bot")));

        BudgetBlockMatch? match = await blocker.EvaluateAsync(Guid.NewGuid(), null, null, CancellationToken.None);

        // The header is the only pre-upstream attribution signal; without it the scope is unknown.
        match.Should().BeNull();
    }

    [TestMethod]
    public async Task Evaluate_AgentScopedBlock_WithMatchingHeader_Applies()
    {
        var blocker = new BudgetBlocker(BlocksOf(AgentBlock("Support bot")));

        BudgetBlockMatch? match = await blocker.EvaluateAsync(
            Guid.NewGuid(), "Support bot", null, CancellationToken.None);

        match.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(match);
        match.AgentName.Should().Be("Support bot");
    }

    [TestMethod]
    public async Task Evaluate_AgentScopedBlock_MatchesHeaderCaseInsensitively()
    {
        var blocker = new BudgetBlocker(BlocksOf(AgentBlock("Support Bot")));

        BudgetBlockMatch? match = await blocker.EvaluateAsync(
            Guid.NewGuid(), "support bot", null, CancellationToken.None);

        match.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Evaluate_AgentScopedBlock_WithDifferentAgentHeader_DoesNotApply()
    {
        var blocker = new BudgetBlocker(BlocksOf(AgentBlock("Support bot")));

        BudgetBlockMatch? match = await blocker.EvaluateAsync(
            Guid.NewGuid(), "Billing bot", null, CancellationToken.None);

        match.Should().BeNull();
    }

    [TestMethod]
    public async Task Evaluate_WithNoBlocks_Passes()
    {
        var blocker = new BudgetBlocker(BlocksOf());

        BudgetBlockMatch? match = await blocker.EvaluateAsync(
            Guid.NewGuid(), "Support bot", null, CancellationToken.None);

        match.Should().BeNull();
    }

    [TestMethod]
    public async Task Evaluate_MixedBlocks_ReturnsTheOneThatApplies()
    {
        BudgetHardBlock agentBlock = AgentBlock("Billing bot");
        var blocker = new BudgetBlocker(BlocksOf(agentBlock, ProjectBlock()));

        BudgetBlockMatch? match = await blocker.EvaluateAsync(
            Guid.NewGuid(), "Support bot", null, CancellationToken.None);

        // The agent-scoped block names a different agent, so the project-wide one is what bites.
        match.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(match);
        match.CostLimitId.Should().NotBe(agentBlock.CostLimitId);
    }

    // ── key scope ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Evaluate_KeyScopedBlock_WithMatchingKey_Applies()
    {
        var keyId = Guid.NewGuid();
        var blocker = new BudgetBlocker(BlocksOf(ApiKeyBlock(keyId)));

        BudgetBlockMatch? match = await blocker.EvaluateAsync(
            Guid.NewGuid(), agentName: null, apiKeyId: keyId, CancellationToken.None);

        // No header needed: every proxied request authenticates with a key, so unlike agent scope
        // this block cannot be evaded by omitting anything.
        match.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(match);
        match.ApiKeyId.Should().Be(keyId);
    }

    [TestMethod]
    public async Task Evaluate_KeyScopedBlock_WithDifferentKey_DoesNotApply()
    {
        var blocker = new BudgetBlocker(BlocksOf(ApiKeyBlock(Guid.NewGuid())));

        BudgetBlockMatch? match = await blocker.EvaluateAsync(
            Guid.NewGuid(), agentName: null, apiKeyId: Guid.NewGuid(), CancellationToken.None);

        match.Should().BeNull();
    }

    [TestMethod]
    public async Task Evaluate_KeyScopedBlock_WithNoKey_DoesNotApply()
    {
        var blocker = new BudgetBlocker(BlocksOf(ApiKeyBlock(Guid.NewGuid())));

        // The upstream-key auth path carries no Proxytrace key. Two nulls must not compare equal
        // here — that would silently turn one key's budget into a block on all unattributed traffic.
        BudgetBlockMatch? match = await blocker.EvaluateAsync(
            Guid.NewGuid(), agentName: null, apiKeyId: null, CancellationToken.None);

        match.Should().BeNull();
    }

    [TestMethod]
    public async Task Evaluate_KeyScopedBlock_DoesNotMatchOnAgentHeader()
    {
        var blocker = new BudgetBlocker(BlocksOf(ApiKeyBlock(Guid.NewGuid())));

        BudgetBlockMatch? match = await blocker.EvaluateAsync(
            Guid.NewGuid(), agentName: "Support bot", apiKeyId: Guid.NewGuid(), CancellationToken.None);

        match.Should().BeNull();
    }

    [TestMethod]
    public async Task Evaluate_UnattributedKeyTraffic_StillCaughtByProjectBlock()
    {
        var blocker = new BudgetBlocker(BlocksOf(ApiKeyBlock(Guid.NewGuid()), ProjectBlock()));

        BudgetBlockMatch? match = await blocker.EvaluateAsync(
            Guid.NewGuid(), agentName: null, apiKeyId: null, CancellationToken.None);

        // The documented backstop: upstream-key traffic escapes key scope but never project scope.
        match.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(match);
        match.ApiKeyId.Should().BeNull();
    }

    private static BudgetHardBlock ProjectBlock()
        => new(Guid.NewGuid(), null, null);

    private static BudgetHardBlock AgentBlock(string agentName)
        => new(Guid.NewGuid(), Guid.NewGuid(), agentName);

    private static BudgetHardBlock ApiKeyBlock(Guid apiKeyId)
        => new(Guid.NewGuid(), null, null, apiKeyId);

    private static IBudgetBlockProvider BlocksOf(params BudgetHardBlock[] blocks)
    {
        var provider = Substitute.For<IBudgetBlockProvider>();
        provider.GetBlocksAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(blocks);
        return provider;
    }
}
