using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;
using Proxytrace.Domain.OptimizationProposal;

namespace Proxytrace.Domain.OptimizationTheory.Internal;

internal class OptimizationTheoryGenerator : DomainEntityGenerator<IOptimizationTheory>, IOptimizationTheoryGenerator
{
    private readonly IDomainEntityGenerator<ISystemPromptTheory> systemPromptGenerator;
    private readonly IDomainEntityGenerator<IToolUpdateTheory> toolUpdateGenerator;
    private readonly IDomainEntityGenerator<IModelSwitchTheory> modelSwitchGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="OptimizationTheoryGenerator"/> class.
    /// </summary>
    public OptimizationTheoryGenerator(
        IRepository<IOptimizationTheory> repository,
        IDomainEntityGenerator<ISystemPromptTheory> systemPromptGenerator,
        IDomainEntityGenerator<IToolUpdateTheory> toolUpdateGenerator,
        IDomainEntityGenerator<IModelSwitchTheory> modelSwitchGenerator,
        IRandom random) : base(repository, random)
    {
        this.systemPromptGenerator = systemPromptGenerator;
        this.toolUpdateGenerator = toolUpdateGenerator;
        this.modelSwitchGenerator = modelSwitchGenerator;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override async Task<IOptimizationTheory> GenerateAsync(CancellationToken cancellationToken = default)
        => await systemPromptGenerator.GenerateAsync(cancellationToken);

    /// <summary>
    /// Creates asynchronously.
    /// </summary>
    public async Task<IOptimizationTheory> CreateAsync(ProposalKind kind, CancellationToken cancellationToken = default)
        => kind switch
        {
            ProposalKind.SystemPrompt => await systemPromptGenerator.CreateAsync(cancellationToken),
            ProposalKind.Tool => await toolUpdateGenerator.CreateAsync(cancellationToken),
            ProposalKind.ModelSwitch => await modelSwitchGenerator.CreateAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
}
