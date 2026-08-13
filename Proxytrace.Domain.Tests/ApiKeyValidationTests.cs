using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class ApiKeyValidationTests : BaseTest<Module>
{
    [TestMethod]
    public async Task CreateNew_WithValidArgs_StoresKeyHashAndPrefix()
    {
        var services = GetServices();
        var (project, provider, owner) = await FkTargets(services);
        var factory = services.GetRequiredService<IApiKey.CreateNew>();

        var key = factory("ci", "HASHVALUE", "proxytrace-AbCd", project, provider, ApiKeyScopes.Ingestion, owner);

        key.KeyHash.Should().Be("HASHVALUE");
        key.KeyPrefix.Should().Be("proxytrace-AbCd");
        key.Owner.Id.Should().Be(owner.Id);
    }

    [TestMethod]
    public async Task CreateNew_WithEmptyKeyHash_Throws()
    {
        var services = GetServices();
        var (project, provider, owner) = await FkTargets(services);
        var factory = services.GetRequiredService<IApiKey.CreateNew>();

        var act = () => factory("ci", "", "proxytrace-AbCd", project, provider, ApiKeyScopes.Ingestion, owner);

        act.Should().Throw<Exception>();
    }

    private async Task<(IProject, IModelProvider, IUser)> FkTargets(IServiceProvider services)
    {
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);
        var owner = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        return (project, provider, owner);
    }
}
