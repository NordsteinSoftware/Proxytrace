using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using Proxytrace.Api.Mcp;
using Proxytrace.Domain;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.Project;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests.Mcp;

[TestClass]
public sealed class McpProjectAccessorTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetProjectAsync_WithStashedProjectId_ResolvesProject()
    {
        IServiceProvider services = GetServices();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);

        var context = new DefaultHttpContext();
        context.Items[McpProjectAccessor.ProjectIdItemKey] = project.Id;
        var accessor = new McpProjectAccessor(
            new HttpContextAccessor { HttpContext = context },
            services.GetRequiredService<IProjectRepository>());

        var resolved = await accessor.GetProjectAsync(CancellationToken);

        resolved.Id.Should().Be(project.Id);
    }

    [TestMethod]
    public async Task GetProjectAsync_WithoutStashedProjectId_Throws()
    {
        IServiceProvider services = GetServices();
        var accessor = new McpProjectAccessor(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            services.GetRequiredService<IProjectRepository>());

        await FluentActions
            .Invoking(() => accessor.GetProjectAsync(CancellationToken))
            .Should().ThrowAsync<McpException>();
    }

    [TestMethod]
    public void RequireWriteScope_WithoutWriteScope_Throws()
    {
        IServiceProvider services = GetServices();
        var context = new DefaultHttpContext();
        context.Items[McpProjectAccessor.ScopesItemKey] = ApiKeyScopes.McpRead;
        var accessor = new McpProjectAccessor(
            new HttpContextAccessor { HttpContext = context },
            services.GetRequiredService<IProjectRepository>());

        FluentActions.Invoking(accessor.RequireWriteScope).Should().Throw<McpException>();
    }

    [TestMethod]
    public void RequireWriteScope_WithWriteScope_DoesNotThrow()
    {
        IServiceProvider services = GetServices();
        var context = new DefaultHttpContext();
        context.Items[McpProjectAccessor.ScopesItemKey] = ApiKeyScopes.McpRead | ApiKeyScopes.McpWrite;
        var accessor = new McpProjectAccessor(
            new HttpContextAccessor { HttpContext = context },
            services.GetRequiredService<IProjectRepository>());

        FluentActions.Invoking(accessor.RequireWriteScope).Should().NotThrow();
    }
}
