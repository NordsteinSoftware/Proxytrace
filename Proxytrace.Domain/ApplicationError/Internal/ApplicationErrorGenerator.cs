using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.ApplicationError.Internal;

internal class ApplicationErrorGenerator : DomainEntityGenerator<IApplicationError>, IApplicationErrorGenerator
{
    private readonly IApplicationError.CreateNew factory;
    private readonly IApplicationError.CreateExisting createExisting;

    public ApplicationErrorGenerator(
        IApplicationError.CreateNew factory,
        IApplicationError.CreateExisting createExisting,
        IRepository<IApplicationError> repository,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.createExisting = createExisting;
    }

    public override Task<IApplicationError> GenerateAsync(CancellationToken cancellationToken = default)
        => factory(
                message: $"Something failed: {random.UniqueString()}",
                level: random.Enum<ApplicationErrorLevel>(),
                category: $"Proxytrace.Test.{random.UniqueString()}",
                exceptionType: "System.InvalidOperationException",
                stackTrace: $"   at Proxytrace.Test.{random.UniqueString()}()")
            .ToTaskResult();

    public async Task<IApplicationError> CreateAsync(DateTimeOffset createdAt, CancellationToken cancellationToken = default)
    {
        var error = await CreateAsync(cancellationToken);
        var modified = createExisting(
            error.Message,
            error.Level,
            error.Category,
            error.ExceptionType,
            error.StackTrace,
            new ModifiedDomainEntityData(error.Id, createdAt, error.UpdatedAt));
        return await modified.UpdateAsync(cancellationToken);
    }

    private record ModifiedDomainEntityData(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt) : IDomainEntityData;
}
