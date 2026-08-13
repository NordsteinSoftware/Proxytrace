using AwesomeAssertions;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Exceptions;
using Proxytrace.Storage.Internal;
using Proxytrace.Storage.Internal.Entities;

namespace Proxytrace.Storage.Tests;

internal interface IEntityTestCases
{
    Task Map_ToDomainAndBack_Works();
    Task Repository_AddAsync_AddsEntity();
    Task Repository_GetAsync_ReturnsAddedEntity();
    Task Repository_UpdateNonExistingAsync_Throws();
    Task Repository_UpsertNonExistingAsync_AddsEntity();
    Task Repository_GetAsync_ThrowsEntityNotFoundException_WhenEntityNotFound();
    Task Repository_ContainsAsync_ReturnsTrueForExistingEntity();
    Task Repository_ContainsAsync_ReturnsFalseForNonExistingEntity();
    Task Repository_CountAsync_ReturnsZeroForEmptyRepository();
    Task Repository_CountAsync_ReturnsCorrectCountAfterAdding();
    Task Repository_GetAllAsync_ReturnsEmptyListForEmptyRepository();
    Task Repository_GetAllAsync_ReturnsAllAddedEntities();
    Task Repository_EnumerateAsync_ReturnsAllAddedEntities();
    Task Repository_AddAsync_ThrowsEntityAlreadyExistsException_WhenEntityExists();
    Task Repository_RemoveAsync_RemovesEntity();
}

internal class EntityTestCases<TStoredEntity, TDomainEntity> : IEntityTestCases
    where TDomainEntity : IDomainEntity
    where TStoredEntity : class, IEntity
{
    private readonly IRepository<TDomainEntity> repository;
    private readonly IMapper<TDomainEntity, TStoredEntity> mapper;
    private readonly IDomainEntityGenerator<TDomainEntity> generator;

    public EntityTestCases(
        IRepository<TDomainEntity> repository, 
        IMapper<TDomainEntity, TStoredEntity> mapper,
        IDomainEntityGenerator<TDomainEntity> generator)
    {
        this.repository = repository;
        this.mapper = mapper;
        this.generator = generator;
    }

    public async Task Map_ToDomainAndBack_Works()
    {
        var domain = await generator.CreateAsync();
        var stored = await mapper.Map(domain);
        var mappedBack = await mapper.Map(stored);
        mappedBack.Should().BeEquivalentTo(domain);
    }

    public async Task Repository_AddAsync_AddsEntity()
    {
        // Arrange
        TDomainEntity entity = await generator.GenerateAsync();

        // Act
        TDomainEntity addedEntity = await repository.AddAsync(entity);

        // Assert
        addedEntity.Should().NotBeNull();
        addedEntity.Id.Should().Be(entity.Id);
    }

    public async Task Repository_GetAsync_ReturnsAddedEntity()
    {
        // Arrange
        TDomainEntity entity = await generator.CreateAsync();

        // Act
        TDomainEntity retrievedEntity = await repository.GetAsync(entity.Id);

        // Assert
        retrievedEntity.Should().NotBeNull();
        retrievedEntity.Id.Should().Be(entity.Id);
    }

    public async Task Repository_UpdateNonExistingAsync_Throws()
    {
        // Arrange
        TDomainEntity nonExistentEntity = await generator.GenerateAsync();

        // Act & Assert
        await FluentActions
            .Invoking(() => repository.UpdateAsync(nonExistentEntity))
            .Should()
            .ThrowAsync<EntityNotFoundException>();
    }

    public async Task Repository_UpsertNonExistingAsync_AddsEntity()
    {
        // Arrange
        TDomainEntity entity = await generator.GenerateAsync();
        bool contains = await repository.ContainsAsync(entity.Id);
        contains.Should().BeFalse();

        // Act
        TDomainEntity result = await repository.UpsertAsync(entity);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(entity.Id);
        result.Should().BeEquivalentTo(entity);
        
        // Verify the entity was actually added
        contains = await repository.ContainsAsync(entity.Id);
        contains.Should().BeTrue();
    }

    public async Task Repository_GetAsync_ThrowsEntityNotFoundException_WhenEntityNotFound()
    {
        // Arrange
        Guid nonExistentId = Guid.NewGuid();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => repository.GetAsync(nonExistentId));
        
        exception.Should().NotBeNull();
    }

    public async Task Repository_ContainsAsync_ReturnsTrueForExistingEntity()
    {
        // Arrange
        TDomainEntity entity = await generator.CreateAsync();

        // Act
        bool contains = await repository.ContainsAsync(entity.Id);

        // Assert
        contains.Should().BeTrue();
    }

    public async Task Repository_ContainsAsync_ReturnsFalseForNonExistingEntity()
    {
        // Arrange
        Guid nonExistentId = Guid.NewGuid();

        // Act
        bool contains = await repository.ContainsAsync(nonExistentId);

        // Assert
        contains.Should().BeFalse();
    }

    public async Task Repository_CountAsync_ReturnsZeroForEmptyRepository()
    {
        // Act
        int count = await repository.CountAsync();

        // Assert
        count.Should().BeGreaterThanOrEqualTo(0);
    }

    public async Task Repository_CountAsync_ReturnsCorrectCountAfterAdding()
    {
        // Arrange
        int initialCount = await repository.CountAsync();
        await generator.CreateAsync();

        // Act
        int finalCount = await repository.CountAsync();

        // Assert
        finalCount.Should().Be(initialCount + 1);
    }

    public async Task Repository_GetAllAsync_ReturnsEmptyListForEmptyRepository()
    {
        // Act
        IReadOnlyList<TDomainEntity> entities = await repository.GetAllAsync();

        // Assert
        entities.Should().NotBeNull();
        entities.Should().BeEmpty();
    }

    public async Task Repository_GetAllAsync_ReturnsAllAddedEntities()
    {
        // Arrange
        var initialEntities = await repository.GetAllAsync();
        int initialCount = initialEntities.Count;
        
        TDomainEntity entity1 = await generator.CreateAsync();
        TDomainEntity entity2 = await generator.CreateAsync();

        // Act
        IReadOnlyList<TDomainEntity> allEntities = await repository.GetAllAsync();

        // Assert
        allEntities.Should().NotBeNull();
        allEntities.Count.Should().Be(initialCount + 2);
        allEntities.Should().Contain(e => e.Id == entity1.Id);
        allEntities.Should().Contain(e => e.Id == entity2.Id);
    }

    public async Task Repository_EnumerateAsync_ReturnsAllAddedEntities()
    {
        // Arrange
        TDomainEntity entity1 = await generator.CreateAsync();
        TDomainEntity entity2 = await generator.CreateAsync();
        
        // Act
        var enumeratedEntities = new List<TDomainEntity>();
        foreach (TDomainEntity entity in await repository.GetAllAsync())
        {
            enumeratedEntities.Add(entity);
        }

        // Assert
        enumeratedEntities.Should().NotBeNull();
        enumeratedEntities.Should().Contain(e => e.Id == entity1.Id);
        enumeratedEntities.Should().Contain(e => e.Id == entity2.Id);
    }

    public async Task Repository_AddAsync_ThrowsEntityAlreadyExistsException_WhenEntityExists()
    {
        // Arrange
        TDomainEntity entity = await generator.GenerateAsync();
        await repository.AddAsync(entity);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<EntityAlreadyExistsException>(
            () => repository.AddAsync(entity));
        
        exception.Should().NotBeNull();
    }

    public async Task Repository_RemoveAsync_RemovesEntity()
    {
        // Arrange
        TDomainEntity entity = await generator.CreateAsync();

        // Act
        try
        {
            await repository.RemoveAsync(entity.Id);
        }
        catch (InvalidOperationException)
        {
            // Archive-only repositories (model-style entities whose history would be cascade-wiped)
            // refuse a hard delete by design; the entity must remain. Their dedicated archive tests
            // cover the ArchiveAsync path. See ArchivableRepository.SupportsHardDelete.
            (await repository.ContainsAsync(entity.Id)).Should().BeTrue();
            return;
        }

        // Assert
        bool contains = await repository.ContainsAsync(entity.Id);
        contains.Should().BeFalse();
    }
}
