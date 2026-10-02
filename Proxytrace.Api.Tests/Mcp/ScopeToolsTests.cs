using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using Nordstein.Core.Testing;
using Proxytrace.Api.Dto.AgentCalls;
using Proxytrace.Api.Mcp;
using Proxytrace.Api.Mcp.Tools;
using Proxytrace.Domain;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Scope;

namespace Proxytrace.Api.Tests.Mcp;

[TestClass]
public sealed class ScopeToolsTests : BaseTest<Module>
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
    public async Task ListScopes_ReturnsOnlyAmbientProjectScopes()
    {
        IServiceProvider services = GetServices();
        var projects = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var project = await projects.CreateAsync(CancellationToken);
        var other = await projects.CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();
        await repo.AdmitAsync(project.Id, "support", 100, CancellationToken);
        await repo.AdmitAsync(other.Id, "billing", 100, CancellationToken);

        var tools = new ScopeTools(new StubProjectAccessor(project), repo);
        var result = await tools.ListScopes(CancellationToken);

        result.Select(s => s.Key).Should().Equal("support");
    }

    [TestMethod]
    [DataRow("support-agents")]
    [DataRow("Support Agents")]
    public async Task ScopeArgument_ByKey_ResolvesCanonicalScope(string argument)
    {
        IServiceProvider services = GetServices();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();
        var scopeId = await repo.AdmitAsync(project.Id, "support-agents", 100, CancellationToken);

        var resolved = await McpScopeArgument.ResolveAsync(repo, project.Id, argument, CancellationToken);

        resolved.Should().Be(scopeId);
    }

    [TestMethod]
    public async Task ScopeArgument_ByKey_ResolvesScopeStoredUnderAnyId()
    {
        // Ingestion admits by key, so a row whose id is not the derived one still takes traffic —
        // the MCP argument must find it the same way rather than by re-deriving the id.
        IServiceProvider services = GetServices();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var existing = await services.GetRequiredService<IScope.CreateNew>()("support", project.Id).AddAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();

        var resolved = await McpScopeArgument.ResolveAsync(repo, project.Id, "support", CancellationToken);

        resolved.Should().Be(existing.Id);
    }

    [TestMethod]
    public async Task ScopeArgument_ScopeOfAnotherProject_Throws()
    {
        IServiceProvider services = GetServices();
        var projects = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var project = await projects.CreateAsync(CancellationToken);
        var other = await projects.CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();
        var foreign = await repo.AdmitAsync(other.Id, "support", 100, CancellationToken);

        await FluentActions
            .Invoking(() => McpScopeArgument.ResolveAsync(repo, project.Id, foreign.ToString(), CancellationToken))
            .Should().ThrowAsync<McpException>();
    }

    [TestMethod]
    public async Task ScopeArgument_UnknownKey_Throws()
    {
        IServiceProvider services = GetServices();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();

        await FluentActions
            .Invoking(() => McpScopeArgument.ResolveAsync(repo, project.Id, "nope", CancellationToken))
            .Should().ThrowAsync<McpException>();
    }

    [TestMethod]
    public async Task ScopeArgument_Blank_MeansNoScope()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IScopeRepository>();

        (await McpScopeArgument.ResolveAsync(repo, Guid.NewGuid(), "  ", CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public async Task ListTraces_WithUnknownScope_Throws()
    {
        IServiceProvider services = GetServices();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var tools = new TraceTools(
            new StubProjectAccessor(project),
            services.GetRequiredService<Proxytrace.Domain.AgentCall.IAgentCallRepository>(),
            services.GetRequiredService<AgentCallDtoMapper>(),
            services.GetRequiredService<IScopeRepository>());

        await FluentActions
            .Invoking(() => tools.ListTraces(scope: "nope", cancellationToken: CancellationToken))
            .Should().ThrowAsync<McpException>();
    }
}
