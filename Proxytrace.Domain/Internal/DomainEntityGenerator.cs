using Nordstein.Core.Common.Random;

namespace Proxytrace.Domain.Internal;

/// <summary>
/// Base class for generating domain entities and saving them to the repository
/// </summary>
internal abstract class DomainEntityGenerator<TDomainEntity> :
    DomainObjectGenerator<TDomainEntity>,
    IDomainEntityGenerator<TDomainEntity>
    where TDomainEntity : IDomainEntity
{
    protected readonly IRepository<TDomainEntity> repository;

    protected DomainEntityGenerator(
        IRepository<TDomainEntity> repository,
        IRandom random) : base(random)
    {
        this.repository = repository;
    }

    /// <inheritdoc />
    public override async Task<TDomainEntity> CreateAsync(CancellationToken cancellationToken = default)
    {
        TDomainEntity instance = await GenerateAsync(cancellationToken);
        return await repository.AddAsync(instance, cancellationToken);
    }

    /// <inheritdoc />
    public abstract Task<TDomainEntity> GenerateAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public async Task<TDomainEntity> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        TDomainEntity? existing = await repository
            .FindFirstAsync(cancellationToken);
        
        return existing is not null
            ? existing 
            : await CreateAsync(cancellationToken);
    }
}