using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Internal;

namespace Proxytrace.Domain.Model.Internal;

internal class ModelGenerator : DomainEntityGenerator<IModel>
{
    private static readonly IReadOnlyCollection<string> ModelNames =
    [
        "claude-opus-4-5",
        "claude-sonnet-4-5",
        "claude-haiku-4-5",
        "gpt-4o",
        "gpt-4o-mini",
        "gemini-2.0-flash",
    ];

    private readonly IModel.CreateNew factory;

    public ModelGenerator(
        IModel.CreateNew factory,
        IRepository<IModel> repository,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
    }

    public override Task<IModel> GenerateAsync(CancellationToken cancellationToken = default)
        => factory(name: $"{random.Any(ModelNames)}-{random.UniqueString()}").ToTaskResult();
}


