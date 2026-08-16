using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Model;
using Proxytrace.Domain.ModelProvider;

namespace Proxytrace.Domain.ModelEndpoint.Internal;

internal class ModelEndpointGenerator : DomainEntityGenerator<IModelEndpoint>
{
    private readonly IModelEndpoint.CreateNew factory;
    private readonly IDomainEntityGenerator<IModel> modelGenerator;
    private readonly IDomainEntityGenerator<IModelProvider> providerGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelEndpointGenerator"/> class.
    /// </summary>
    public ModelEndpointGenerator(
        IModelEndpoint.CreateNew factory,
        IRepository<IModelEndpoint> repository,
        IDomainEntityGenerator<IModel> modelGenerator,
        IDomainEntityGenerator<IModelProvider> providerGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.modelGenerator = modelGenerator;
        this.providerGenerator = providerGenerator;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override async Task<IModelEndpoint> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var model = await modelGenerator.GetOrCreateAsync(cancellationToken);
        var provider = await providerGenerator.GetOrCreateAsync(cancellationToken);

        var inputTokenCost = random.Decimal(0, 10);
        return factory(
            model: model,
            provider: provider,
            inputTokenCost: inputTokenCost,
            outputTokenCost: inputTokenCost * random.Decimal(5, 10),
            cachedInputTokenCost: inputTokenCost * random.Decimal(0, 1));
    }
}

