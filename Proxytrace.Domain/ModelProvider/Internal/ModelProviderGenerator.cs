using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.ModelProvider.Internal;

internal class ModelProviderGenerator : DomainEntityGenerator<IModelProvider>
{
    private static readonly IReadOnlyCollection<string> ProviderNames =
    [
        "OpenAI",
        "Google",
        "Azure",
        "Mistral",
    ];

    private readonly IModelProvider.CreateNew factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelProviderGenerator"/> class.
    /// </summary>
    public ModelProviderGenerator(
        IModelProvider.CreateNew factory,
        IRepository<IModelProvider> repository,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override Task<IModelProvider> GenerateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(factory(
            name: $"{random.Any(ProviderNames)}-{random.UniqueString()}",
            endpoint: new Uri($"https://api.{random.Int(1, int.MaxValue)}.example.com/v1"),
            apiKey: random.String(),
            kind: ModelProviderKind.OpenAiCompatible));
}
