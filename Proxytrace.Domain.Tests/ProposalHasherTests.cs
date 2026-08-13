using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.Tools;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class ProposalHasherTests : BaseTest<Module>
{
    [TestMethod]
    public async Task SystemPrompt_SameInputs_ProducesSameHash()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var agent = await GetAgent(services);
        var abRun = await GetTestRun(services);

        var a = factory(agent, Priority.Medium, "r", "prompt-x", 0.5, 0.7, [], abRun);
        var b = factory(agent, Priority.High, "different-rationale", "prompt-x", 0.1, 0.2, [], abRun);

        a.ContentHash.Should().Be(b.ContentHash);
        a.ContentHash.Should().HaveLength(64);
    }

    [TestMethod]
    public async Task SystemPrompt_DifferentMessage_ProducesDifferentHash()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var agent = await GetAgent(services);
        var abRun = await GetTestRun(services);

        var a = factory(agent, Priority.Medium, "r", "prompt-x", null, null, [], abRun);
        var b = factory(agent, Priority.Medium, "r", "prompt-y", null, null, [], abRun);

        a.ContentHash.Should().NotBe(b.ContentHash);
    }

    [TestMethod]
    public async Task SystemPrompt_WhitespaceNormalized()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var agent = await GetAgent(services);
        var abRun = await GetTestRun(services);

        var a = factory(agent, Priority.Medium, "r", "prompt-x\n", null, null, [], abRun);
        var b = factory(agent, Priority.Medium, "r", "  prompt-x\r\n", null, null, [], abRun);

        a.ContentHash.Should().Be(b.ContentHash);
    }

    [TestMethod]
    public async Task ToolUpdate_ToolListOrderingIsInvariant()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IToolUpdateProposal.CreateNew>();
        var agent = await GetAgent(services);
        var abRun = await GetTestRun(services);

        var args = ToolArguments.None;
        var alpha = new ToolSpecification("alpha", "A", args);
        var beta = new ToolSpecification("beta", "B", args);

        var a = factory(agent, Priority.Medium, "r", [alpha, beta], null, null, [], abRun);
        var b = factory(agent, Priority.Medium, "r", [beta, alpha], null, null, [], abRun);

        a.ContentHash.Should().Be(b.ContentHash);
    }

    [TestMethod]
    public async Task ModelSwitch_DifferentEndpointIds_DifferentHash()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IModelSwitchProposal.CreateNew>();
        var agent = await GetAgent(services);
        var abRun = await GetTestRun(services);

        var ep1 = Substitute.For<IModelEndpoint>();
        ep1.Id.Returns(Guid.NewGuid());
        var ep2 = Substitute.For<IModelEndpoint>();
        ep2.Id.Returns(Guid.NewGuid());

        var a = factory(agent, Priority.Medium, "r", ep1, null, null, null, null, [], abRun);
        var b = factory(agent, Priority.Medium, "r", ep2, null, null, null, null, [], abRun);

        a.ContentHash.Should().NotBe(b.ContentHash);
    }

    [TestMethod]
    public async Task DifferentKind_SameAgentAndPayload_DifferentHash()
    {
        IServiceProvider services = GetServices();
        var sp = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var tu = services.GetRequiredService<IToolUpdateProposal.CreateNew>();
        var agent = await GetAgent(services);
        var abRun = await GetTestRun(services);

        var a = sp(agent, Priority.Medium, "r", "x", null, null, [], abRun);
        var args = ToolArguments.None;
        var b = tu(agent, Priority.Medium, "r", [new ToolSpecification("x", "x", args)], null, null, [], abRun);

        a.ContentHash.Should().NotBe(b.ContentHash);
    }

    private static async Task<IAgent> GetAgent(IServiceProvider services)
        => await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().GetOrCreateAsync();

    private async Task<ITestRun> GetTestRun(IServiceProvider services)
        => await services.GetRequiredService<IDomainEntityGenerator<ITestRun>>().CreateAsync(CancellationToken);
}
