using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.Inference.Internal;

internal sealed class ModelParametersGenerator : DomainObjectGenerator<IModelParameters>
{
    private readonly IModelParameters.Create factory;

    public ModelParametersGenerator(
        IRandom random,
        IModelParameters.Create factory) : base(random)
    {
        this.factory = factory;
    }

    public override Task<IModelParameters> CreateAsync(CancellationToken cancellationToken = default)
        => factory(
            temperature: random.Double(min: 0.7, max: 0.9),
            topP: random.Double(min: 0.7, max: 0.9),
            reasoningEffort: random.Any(["none", "low", "medium", "high"]),
            frequencyPenalty: random.Double(min: 0.0, max: 0.5),
            presencePenalty: random.Double(min: 0.0, max: 0.5),
            maxTokens: random.Int(min: 50, max: 200)
            ).ToTaskResult();
}
