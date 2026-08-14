using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Agent;
using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class AgentValidationTests : DomainTest<Module>
{
    [TestMethod]
    public async Task CreateNew_WithValidInputs_CreatesAgent()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IAgent.CreateNew>();
        var promptTemplateFactory = services.GetRequiredService<IPromptTemplate.Create>();
        var modelParametersFactory = services.GetRequiredService<IModelParameters.Create>();
        var systemPrompt = promptTemplateFactory("Test Agent", "You are a helpful assistant");
        var project = await CreateTestProjectAsync(services);
        var endpoint = await GetOrCreate<IModelEndpoint>(services);

        // Act
        var agent = factory(
            "Test Agent",
            systemPrompt,
            [],
            endpoint,
            project,
            modelParametersFactory());

        // Assert
        agent.Should().NotBeNull();
        agent.Name.Should().Be("Test Agent");
        agent.SystemPrompt.Should().Be(systemPrompt);
        agent.Project.Should().Be(project);
        agent.Id.Should().NotBe(Guid.Empty);
        agent.CreatedAt.Should().NotBe(default);
        agent.UpdatedAt.Should().NotBe(default);
    }

    [TestMethod]
    public async Task CreateNew_WithNullSystemMessage_ThrowsValidationException()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IAgent.CreateNew>();
        var modelParametersFactory = services.GetRequiredService<IModelParameters.Create>();
        var project = await CreateTestProjectAsync(services);
        var endpoint = await GetOrCreate<IModelEndpoint>(services);

        // Act & Assert
        var action = () => factory.DynamicInvoke("Test Agent", null, Array.Empty<ToolSpecification>(), endpoint, project, modelParametersFactory(), false);
        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_WithNullProject_ThrowsValidationException()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IAgent.CreateNew>();
        var promptTemplateFactory = services.GetRequiredService<IPromptTemplate.Create>();
        var modelParametersFactory = services.GetRequiredService<IModelParameters.Create>();
        var systemPrompt = promptTemplateFactory("Test Agent", "You are a helpful assistant");
        var endpoint = await GetOrCreate<IModelEndpoint>(services);

        // Act & Assert
        var action = () => factory.DynamicInvoke("Test Agent", systemPrompt, Array.Empty<ToolSpecification>(), endpoint, null, modelParametersFactory(), false);
        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateExisting_WithValidData_CreatesAgent()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var createExisting = services.GetRequiredService<IAgent.CreateExisting>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();
        var existingAgent = await generator.CreateAsync(CancellationToken);

        // Act
        var agent = createExisting(
            existingAgent.Name,
            existingAgent.Project,
            existingAgent.Endpoint,
            existingAgent.IsSystemAgent,
            existingAgent.ModelParameters,
            existingAgent.CurrentVersion,
            existingAgent);

        // Assert
        agent.Should().NotBeNull();
        agent.Id.Should().Be(existingAgent.Id);
        agent.Name.Should().Be(existingAgent.Name);
        agent.Project.Should().Be(existingAgent.Project);
        agent.SystemPrompt.Should().Be(existingAgent.SystemPrompt);
        agent.CreatedAt.Should().Be(existingAgent.CreatedAt);
        agent.UpdatedAt.Should().Be(existingAgent.UpdatedAt);
    }

    [TestMethod]
    public async Task CreateExisting_WithNullProject_ThrowsValidationException()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var createExisting = services.GetRequiredService<IAgent.CreateExisting>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();
        var existingAgent = await generator.CreateAsync(CancellationToken);

        // Act & Assert
        var action = () => createExisting.DynamicInvoke(existingAgent.Name, null, existingAgent.Endpoint, existingAgent.IsSystemAgent, existingAgent.ModelParameters, existingAgent.CurrentVersion, existingAgent.SystemPrompt, existingAgent.Tools, existingAgent);
        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task Id_IsUniqueForEachNewAgent()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IAgent.CreateNew>();
        var promptTemplateFactory = services.GetRequiredService<IPromptTemplate.Create>();
        var modelParametersFactory = services.GetRequiredService<IModelParameters.Create>();
        var systemPrompt = promptTemplateFactory("Test Agent", "You are a helpful assistant");
        var project = await CreateTestProjectAsync(services);
        var endpoint = await GetOrCreate<IModelEndpoint>(services);

        // Act
        var agent1 = factory("Agent One", systemPrompt, [], endpoint, project, modelParametersFactory());
        var agent2 = factory("Agent Two", systemPrompt, [], endpoint, project, modelParametersFactory());

        // Assert
        agent1.Id.Should().NotBe(agent2.Id);
    }

    [TestMethod]
    public async Task ChangeSystemMessage_WithEqualButSeparatelyConstructedPrompt_DoesNotCreateNewVersion()
    {
        // Regression: PromptTemplate's synthesized equality compared the Variables set by
        // reference, so this dedup never matched and every save minted a redundant agent version.
        IServiceProvider services = GetServices();
        var promptTemplateFactory = services.GetRequiredService<IPromptTemplate.Create>();
        var agent = await GetOrCreate<IAgent>(services);
        var samePrompt = promptTemplateFactory(agent.SystemPrompt.Name, agent.SystemPrompt.Template);

        var updated = await agent.ChangeSystemMessage(samePrompt, CancellationToken);

        updated.CurrentVersion.Id.Should().Be(agent.CurrentVersion.Id);
    }

    private async Task<IProject> CreateTestProjectAsync(IServiceProvider services)
    {
        var projectFactory = services.GetRequiredService<IProject.CreateNew>();
        var endpoint = await GetOrCreate<IModelEndpoint>(services);
        return projectFactory("Test Project", endpoint, []);
    }
}
