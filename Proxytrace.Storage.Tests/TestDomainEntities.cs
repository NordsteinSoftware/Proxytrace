using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Storage.Internal.Entities;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public class TestDomainEntities : BaseTest<Module>
{
    protected override void ConfigureContainer(ContainerBuilder builder)
    {
        base.ConfigureContainer(builder);
        builder.RegisterModule<Module>();
        builder.RegisterModule<Domain.Module>();
        
        foreach (var storedEntityType in GetStoredEntities())
        {
            Type? domainEntityType = storedEntityType.GetDomainEntityType();
            if (domainEntityType is null)
            {
                continue;
            }
            var testType = typeof(EntityTestCases<,>).MakeGenericType(storedEntityType, domainEntityType);
            builder.RegisterType(testType);
        }
    }

    private static IReadOnlyCollection<Type> GetStoredEntities()
        => typeof(Storage.Module).Assembly
            .GetTypes()
            .Where(t => t is { IsInterface: false, IsAbstract: false, IsClass: true } && t != typeof(IEntity))
            .Where(t => t.GetInterfaces().Any(i => i == typeof(IEntity)))
            .Where(t => t.GetDomainEntityType() is not null)
            .ToList();
    
    public static IEnumerable<object[]> GetStoredEntityTypes() 
        => GetStoredEntities()
            .Select(storedEntityType => (object[])[storedEntityType]);

    private IEntityTestCases GetTestCases(Type storedEntityType)
    {
        var services = GetServices();
        var domainEntityType = storedEntityType.GetDomainEntityType();
        if (domainEntityType is null)
        {
            return Substitute.For<IEntityTestCases>();
        }
        var testType = typeof(EntityTestCases<,>).MakeGenericType(storedEntityType, domainEntityType);
        var testCases = (IEntityTestCases)services.GetRequiredService(testType);
        return testCases;
    }

    [TestMethod]
    [DynamicData(nameof(GetStoredEntityTypes))]
    public Task Map_ToDomainAndBack_Works(Type storedEntityType) 
        => GetTestCases(storedEntityType).Map_ToDomainAndBack_Works();
    
    [TestMethod]
    [DynamicData(nameof(GetStoredEntityTypes))]
    public Task Repository_AddAsync_AddsEntity(Type storedEntityType)
        => GetTestCases(storedEntityType).Repository_AddAsync_AddsEntity();
    
    [TestMethod]
    [DynamicData(nameof(GetStoredEntityTypes))]
    public Task Repository_GetAsync_ReturnsAddedEntity(Type storedEntityType)
        => GetTestCases(storedEntityType).Repository_GetAsync_ReturnsAddedEntity();
    
    [TestMethod]
    [DynamicData(nameof(GetStoredEntityTypes))]
    public Task Repository_GetAsync_ThrowsEntityNotFoundException_WhenEntityNotFound(Type storedEntityType)
        => GetTestCases(storedEntityType).Repository_GetAsync_ThrowsEntityNotFoundException_WhenEntityNotFound();
    
    [TestMethod]
    [DynamicData(nameof(GetStoredEntityTypes))]
    public Task Repository_ContainsAsync_ReturnsTrueForExistingEntity(Type storedEntityType)
        => GetTestCases(storedEntityType).Repository_ContainsAsync_ReturnsTrueForExistingEntity();
    
    [TestMethod]
    [DynamicData(nameof(GetStoredEntityTypes))]
    public Task Repository_ContainsAsync_ReturnsFalseForNonExistingEntity(Type storedEntityType)
        => GetTestCases(storedEntityType).Repository_ContainsAsync_ReturnsFalseForNonExistingEntity();
    
    [TestMethod]
    [DynamicData(nameof(GetStoredEntityTypes))]
    public Task Repository_CountAsync_ReturnsZeroForEmptyRepository(Type storedEntityType)
        => GetTestCases(storedEntityType).Repository_CountAsync_ReturnsZeroForEmptyRepository();
    
    [TestMethod]
    [DynamicData(nameof(GetStoredEntityTypes))]
    public Task Repository_CountAsync_ReturnsCorrectCountAfterAdding(Type storedEntityType)
        => GetTestCases(storedEntityType).Repository_CountAsync_ReturnsCorrectCountAfterAdding();
    
    [TestMethod]
    [DynamicData(nameof(GetStoredEntityTypes))]
    public Task Repository_GetAllAsync_ReturnsEmptyListForEmptyRepository(Type storedEntityType)
        => GetTestCases(storedEntityType).Repository_GetAllAsync_ReturnsEmptyListForEmptyRepository();
    
    [TestMethod]
    [DynamicData(nameof(GetStoredEntityTypes))]
    public Task Repository_GetAllAsync_ReturnsAllAddedEntities(Type storedEntityType)
        => GetTestCases(storedEntityType).Repository_GetAllAsync_ReturnsAllAddedEntities();
    
    [TestMethod]
    [DynamicData(nameof(GetStoredEntityTypes))]
    public Task Repository_EnumerateAsync_ReturnsAllAddedEntities(Type storedEntityType)
        => GetTestCases(storedEntityType).Repository_EnumerateAsync_ReturnsAllAddedEntities();
    
    [TestMethod]
    [DynamicData(nameof(GetStoredEntityTypes))]
    public Task Repository_AddAsync_ThrowsEntityAlreadyExistsException_WhenEntityExists(Type storedEntityType)
        => GetTestCases(storedEntityType).Repository_AddAsync_ThrowsEntityAlreadyExistsException_WhenEntityExists();
    
    [TestMethod]
    [DynamicData(nameof(GetStoredEntityTypes))]
    public Task Repository_UpdateNonExistingAsync_Throws(Type storedEntityType)
        => GetTestCases(storedEntityType).Repository_UpdateNonExistingAsync_Throws();
    
    [TestMethod]
    [DynamicData(nameof(GetStoredEntityTypes))]
    public Task Repository_UpsertNonExistingAsync_AddsEntity(Type storedEntityType)
        => GetTestCases(storedEntityType).Repository_UpsertNonExistingAsync_AddsEntity();
    
    [TestMethod]
    [DynamicData(nameof(GetStoredEntityTypes))]
    public Task Repository_RemoveAsync_RemovesEntity(Type storedEntityType)
        => GetTestCases(storedEntityType).Repository_RemoveAsync_RemovesEntity();
}