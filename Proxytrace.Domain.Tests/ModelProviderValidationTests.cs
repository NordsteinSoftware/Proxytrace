using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.ModelProvider;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class ModelProviderValidationTests : DomainTest<Module>
{
    private static readonly Uri Endpoint = new("https://api.example.com/v1");

    [TestMethod]
    public void CreateNew_WithAnEmptyApiKey_Throws()
    {
        // Every creation path requires a key; the empty-key tolerance below is only for providers
        // reconstituted from storage whose ciphertext can no longer be decrypted.
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IModelProvider.CreateNew>();

        var action = () => factory("p", Endpoint, string.Empty, ModelProviderKind.OpenAiCompatible);

        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateExisting_WithAnUnavailableApiKey_LoadsWithTheKeyUnset()
    {
        // A provider whose stored key could not be decrypted (lost Data Protection key ring) must
        // still load: reads degrade to "key unset" instead of failing validation (docs/security.md).
        IServiceProvider services = GetServices();
        var createExisting = services.GetRequiredService<IModelProvider.CreateExisting>();
        var existingProvider = await GetOrCreate<IModelProvider>(services);

        var provider = createExisting(
            existingProvider.Name,
            existingProvider.Endpoint,
            string.Empty,
            existingProvider.Kind,
            existingProvider,
            apiKeyUnavailable: true);

        provider.Should().NotBeNull();
        provider.Id.Should().Be(existingProvider.Id);
        provider.ApiKey.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CreateExisting_WithoutTheUnavailableFlag_WithAnEmptyKey_Throws()
    {
        // The default stays strict so an update built from operator input cannot silently clear a
        // provider's credential.
        IServiceProvider services = GetServices();
        var createExisting = services.GetRequiredService<IModelProvider.CreateExisting>();
        var existingProvider = await GetOrCreate<IModelProvider>(services);

        var action = () => createExisting(
            existingProvider.Name,
            existingProvider.Endpoint,
            string.Empty,
            existingProvider.Kind,
            existingProvider);

        action.Should().Throw<Exception>();
    }
}
