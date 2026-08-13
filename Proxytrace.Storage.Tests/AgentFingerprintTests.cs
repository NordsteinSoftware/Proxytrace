using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Inference;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.Tools;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// Tests for agent fingerprinting and grouping behavior.
/// Verifies that traced calls are correctly grouped under the same agent when their
/// identity fields match, and that a new agent version is created when any field changes.
/// </summary>
[TestClass]
public sealed class AgentFingerprintTests : BaseTest<Module>
{
    protected override void ConfigureContainer(ContainerBuilder builder)
    {
        base.ConfigureContainer(builder);
        builder.RegisterModule<Module>();
        builder.RegisterModule<Domain.Module>();
        builder.RegisterInstance<IAgentNameGenerator>(new StubNameGenerator()).SingleInstance();
    }

    private sealed class StubNameGenerator : IAgentNameGenerator
    {
        public Task<string> GenerateNameAsync(IPromptTemplate systemPrompt, IProject project, CancellationToken cancellationToken = default)
            => Task.FromResult<string>("Test Agent");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<IProject> CreateProjectAsync(IServiceProvider services, CancellationToken ct)
        => await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(ct);

    private static async Task<IModelEndpoint> CreateEndpointAsync(IServiceProvider services, CancellationToken ct)
        => await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(ct);

    private static IPromptTemplate MakePrompt(IServiceProvider services, string content)
        => services.GetRequiredService<IPromptTemplate.Create>()("test-prompt", content);

    private static ToolSpecification MakeTool(string name)
        => new(name, $"{name} description", ToolArguments.None);

    // ── fingerprint determinism ───────────────────────────────────────────────

    [TestMethod]
    public void GetAgentFingerprint_SameInputs_ReturnsSameFingerprint()
    {
        var services = GetServices();
        var repo = services.GetRequiredService<IAgentRepository>();
        var msg  = MakePrompt(services, "You are a helpful assistant");
        var tools = (IReadOnlyCollection<ToolSpecification>)[MakeTool("search")];

        var fp1 = repo.GetAgentFingerprint(msg, tools);
        var fp2 = repo.GetAgentFingerprint(msg, tools);

        fp1.Should().Be(fp2);
    }

    [TestMethod]
    public void GetAgentFingerprint_IsLowercaseHex64Chars()
    {
        var services = GetServices();
        var repo = services.GetRequiredService<IAgentRepository>();
        var fp = repo.GetAgentFingerprint(MakePrompt(services, "test"), []);

        fp.Should().HaveLength(64);
        fp.Should().MatchRegex("^[0-9a-f]+$");
    }

    // ── version-sensitive changes ─────────────────────────────────────────────

    [TestMethod]
    public void GetAgentFingerprint_DifferentSystemMessage_ReturnsDifferentFingerprint()
    {
        var services = GetServices();
        var repo = services.GetRequiredService<IAgentRepository>();

        var fp1 = repo.GetAgentFingerprint(MakePrompt(services, "You are agent A"), []);
        var fp2 = repo.GetAgentFingerprint(MakePrompt(services, "You are agent B"), []);

        fp1.Should().NotBe(fp2);
    }

    [TestMethod]
    public void GetAgentFingerprint_DifferentTools_ReturnsDifferentFingerprint()
    {
        var services = GetServices();
        var repo = services.GetRequiredService<IAgentRepository>();
        var msg = MakePrompt(services, "You are a helpful assistant");

        var fp1 = repo.GetAgentFingerprint(msg, [MakeTool("search")]);
        var fp2 = repo.GetAgentFingerprint(msg, [MakeTool("calculator")]);

        fp1.Should().NotBe(fp2);
    }

    [TestMethod]
    public void GetAgentFingerprint_ToolOrderDoesNotMatter()
    {
        var services = GetServices();
        var repo = services.GetRequiredService<IAgentRepository>();
        var msg = MakePrompt(services, "You are a helpful assistant");
        var toolA = MakeTool("aaa");
        var toolB = MakeTool("bbb");

        var fp1 = repo.GetAgentFingerprint(msg, [toolA, toolB]);
        var fp2 = repo.GetAgentFingerprint(msg, [toolB, toolA]);

        fp1.Should().Be(fp2);
    }

    // ── GetOrCreateAsync grouping ─────────────────────────────────────────────

    [TestMethod]
    public async Task GetOrCreateAsync_SameInputs_ReturnsSameAgent()
    {
        var services = GetServices();
        var repo     = services.GetRequiredService<IAgentRepository>();
        var project  = await CreateProjectAsync(services, CancellationToken);
        var endpoint = await CreateEndpointAsync(services, CancellationToken);
        var msg      = MakePrompt(services, "You are a helpful assistant");

        var agent1 = await repo.GetOrCreateAsync(msg, [], project, endpoint, cancellationToken: CancellationToken);
        var agent2 = await repo.GetOrCreateAsync(msg, [], project, endpoint, cancellationToken: CancellationToken);

        agent1.Id.Should().Be(agent2.Id);
    }

    [TestMethod]
    public async Task GetOrCreateAsync_DifferentSystemMessage_CreatesSeparateAgent()
    {
        var services = GetServices();
        var repo     = services.GetRequiredService<IAgentRepository>();
        var project  = await CreateProjectAsync(services, CancellationToken);
        var endpoint = await CreateEndpointAsync(services, CancellationToken);

        var agent1 = await repo.GetOrCreateAsync(MakePrompt(services, "You are agent A"), [], project, endpoint, cancellationToken: CancellationToken);
        var agent2 = await repo.GetOrCreateAsync(MakePrompt(services, "You are agent B"), [], project, endpoint, cancellationToken: CancellationToken);

        agent1.Id.Should().NotBe(agent2.Id);
    }

    [TestMethod]
    public async Task GetOrCreateAsync_CreatedAgentHasCorrectFields()
    {
        var services = GetServices();
        var repo     = services.GetRequiredService<IAgentRepository>();
        var project  = await CreateProjectAsync(services, CancellationToken);
        var endpoint = await CreateEndpointAsync(services, CancellationToken);
        var msg      = MakePrompt(services, "You are a helpful assistant");

        var agent = await repo.GetOrCreateAsync(msg, [MakeTool("search")], project, endpoint, cancellationToken: CancellationToken);

        agent.Name.Should().Be("Test Agent");
        agent.Tools.Should().HaveCount(1);
        agent.Project.Id.Should().Be(project.Id);
    }

    [TestMethod]
    public async Task GetOrCreateAsync_DifferentModelParameters_ReturnsSameAgent()
    {
        var services = GetServices();
        var repo     = services.GetRequiredService<IAgentRepository>();
        var project  = await CreateProjectAsync(services, CancellationToken);
        var endpoint = await CreateEndpointAsync(services, CancellationToken);
        var msg      = MakePrompt(services, "You are a helpful assistant");

        var paramsFactory = services.GetRequiredService<IModelParameters.Create>();
        var withTemp02 = paramsFactory(temperature: 0.2);
        var withTemp07 = paramsFactory(temperature: 0.7);

        var agent1 = await repo.GetOrCreateAsync(msg, [], project, endpoint,
            modelParameters: withTemp02, cancellationToken: CancellationToken);
        var agent2 = await repo.GetOrCreateAsync(msg, [], project, endpoint,
            modelParameters: withTemp07, cancellationToken: CancellationToken);

        agent1.Id.Should().Be(agent2.Id);
        repo.GetAgentFingerprint(agent1).Should().Be(repo.GetAgentFingerprint(agent2));
    }

    [TestMethod]
    public async Task GetAgentFingerprint_OnAgent_MatchesComputedFingerprint()
    {
        var services = GetServices();
        var repo     = services.GetRequiredService<IAgentRepository>();
        var project  = await CreateProjectAsync(services, CancellationToken);
        var endpoint = await CreateEndpointAsync(services, CancellationToken);
        var msg      = MakePrompt(services, "You are a helpful assistant");

        var agent    = await repo.GetOrCreateAsync(msg, [], project, endpoint, cancellationToken: CancellationToken);
        var expected = repo.GetAgentFingerprint(msg, []);

        repo.GetAgentFingerprint(agent).Should().Be(expected);
    }
}
