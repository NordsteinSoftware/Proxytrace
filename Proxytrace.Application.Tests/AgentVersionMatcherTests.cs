using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Application.Ingestion.Internal;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.Tools;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests;

[TestClass]
public class AgentVersionMatcherTests : BaseTest<Module>
{
    [TestMethod]
    public async Task FindsSimilarVersion_WhenSystemPromptIsClose_AndToolSchemaUnchanged()
    {
        var services = GetServices();
        var matcher = services.GetRequiredService<IAgentVersionMatcher>();
        var agents = services.GetRequiredService<IAgentRepository>();
        var promptCreate = services.GetRequiredService<IPromptTemplate.Create>();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);

        var tools = new ToolSpecification[]
        {
            new("lookup", "Looks up an order", ToolArguments.None),
        };
        var v1Prompt = promptCreate("agent", "You are a helpful support agent that answers customer order questions.");
        await agents.GetOrCreateAsync(v1Prompt, tools, project, endpoint, cancellationToken: CancellationToken);

        var v2Prompt = promptCreate("agent", "You are a helpful support agent that answers customer order questions promptly.");
        var match = await matcher.FindSimilarVersionAsync(project, v2Prompt, tools, CancellationToken);

        match.Should().NotBeNull();
        match.SystemPrompt.Template.Should().Be(v1Prompt.Template);
    }

    [TestMethod]
    public async Task NoMatch_WhenPromptDiffersTooMuch()
    {
        var services = GetServices();
        var matcher = services.GetRequiredService<IAgentVersionMatcher>();
        var agents = services.GetRequiredService<IAgentRepository>();
        var promptCreate = services.GetRequiredService<IPromptTemplate.Create>();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);

        await agents.GetOrCreateAsync(
            promptCreate("a", "You are a helpful customer support assistant."), [], project, endpoint, cancellationToken: CancellationToken);

        var match = await matcher.FindSimilarVersionAsync(
            project,
            promptCreate("a", "You are a meticulous codebase reviewer focusing on security audits."),
            [],
            CancellationToken);

        match.Should().BeNull();
    }
}
