using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using Proxytrace.Api.Dto.Agents;
using Proxytrace.Api.Mcp;
using Proxytrace.Api.Mcp.Tools;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Project;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests.Mcp;

[TestClass]
public sealed class AgentToolsTests : BaseTest<Module>
{
    private sealed class StubProjectAccessor : IMcpProjectAccessor
    {
        private readonly IProject project;

        public StubProjectAccessor(IProject project)
        {
            this.project = project;
        }

        public Task<IProject> GetProjectAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(project);

        public void RequireWriteScope()
        {
        }
    }

    [TestMethod]
    public async Task ListAgents_ReturnsOnlyAmbientProjectAgents()
    {
        IServiceProvider services = GetServices();
        var agentGen = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();
        var projectGen = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var agentFactory = services.GetRequiredService<IAgent.CreateNew>();
        var agentRepo = services.GetRequiredService<IAgentRepository>();

        var inProject = await agentGen.CreateAsync(CancellationToken);
        var otherProject = await projectGen.CreateAsync(CancellationToken);
        var otherAgent = await agentRepo.AddAsync(
            agentFactory(inProject.Name + "-other", inProject.SystemPrompt, inProject.Tools, inProject.Endpoint, otherProject, inProject.ModelParameters),
            CancellationToken);

        var tools = BuildTools(services, inProject.Project);
        var result = await tools.ListAgents(CancellationToken);

        result.Should().ContainSingle(a => a.Id == inProject.Id);
        result.Should().NotContain(a => a.Id == otherAgent.Id);
    }

    [TestMethod]
    public async Task GetAgent_FromDifferentProject_Throws()
    {
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var otherProject = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);

        var tools = BuildTools(services, otherProject);

        await FluentActions
            .Invoking(() => tools.GetAgent(agent.Id, CancellationToken))
            .Should().ThrowAsync<McpException>();
    }

    [TestMethod]
    public async Task GetAgent_InAmbientProject_ReturnsAgent()
    {
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

        var tools = BuildTools(services, agent.Project);
        var result = await tools.GetAgent(agent.Id, CancellationToken);

        result.Id.Should().Be(agent.Id);
    }

    private static AgentTools BuildTools(IServiceProvider services, IProject project)
        => new(
            new StubProjectAccessor(project),
            services.GetRequiredService<IAgentRepository>(),
            services.GetRequiredService<IAgentCallRepository>(),
            services.GetRequiredService<AgentDtoMapper>());
}
