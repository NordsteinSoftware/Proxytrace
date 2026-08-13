using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Security;
using Proxytrace.Domain;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class ApiKeySecretTests : BaseTest<Module>
{
    [TestMethod]
    public async Task FindByKey_MatchesOnHash_NotPlaintext()
    {
        IServiceProvider services = GetServices();
        var keys = services.GetRequiredService<IApiKeyRepository>();
        var create = services.GetRequiredService<IApiKey.CreateNew>();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);
        var owner = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);

        const string raw = "proxytrace-raw-value-123";
        var saved = await keys.AddAsync(
            create("ci", Sha256.HexHash(raw), raw[..16], project, provider, ApiKeyScopes.Ingestion, owner),
            CancellationToken);

        // The presented raw key resolves (the repository hashes it before matching)...
        var byRaw = await keys.FindByKeyAsync(raw, CancellationToken);
        byRaw.Should().NotBeNull();
        byRaw?.Id.Should().Be(saved.Id);

        // ...but the stored hash itself is not a valid key.
        (await keys.FindByKeyAsync(saved.KeyHash, CancellationToken)).Should().BeNull();
    }
}
