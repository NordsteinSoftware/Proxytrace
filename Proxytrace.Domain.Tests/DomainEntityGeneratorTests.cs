using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Proxytrace.Storage;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class DomainEntityGeneratorTests : BaseTest<Module>
{
    protected override void ConfigureContainer(ContainerBuilder builder)
    {
        base.ConfigureContainer(builder);
        builder.RegisterModule(new Storage.Module(_ => StorageConfiguration.InMemory()));
    }

    // User Generator Tests

    [TestMethod]
    public async Task UserGenerator_GenerateAsync_CreatesValidUser()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IUser>>();

        // Act
        var user = await generator.CreateAsync(CancellationToken);

        // Assert
        user.Should().NotBeNull();
        user.Id.Should().NotBe(Guid.Empty);
        user.Email.Should().NotBeNullOrWhiteSpace();
        user.CreatedAt.Should().NotBe(default);
        user.UpdatedAt.Should().NotBe(default);
    }

    [TestMethod]
    public async Task UserGenerator_CreateAsync_CreatesAndPersistsUser()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IUser>>();
        var repository = services.GetRequiredService<IRepository<IUser>>();

        // Act
        var user = await generator.CreateAsync(CancellationToken);

        // Assert
        user.Should().NotBeNull();
        user.Id.Should().NotBe(Guid.Empty);

        // Verify it was persisted
        var retrieved = await repository.GetAsync(user.Id, CancellationToken);
        retrieved.Should().NotBeNull();
        retrieved.Id.Should().Be(user.Id);
        retrieved.Email.Should().Be(user.Email);
    }

    [TestMethod]
    public async Task UserGenerator_GetOrCreateAsync_CreatesNewUserFirstTime()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IUser>>();
        var repository = services.GetRequiredService<IRepository<IUser>>();
        var initialCount = await repository.CountAsync(CancellationToken);

        // Act
        var user = await generator.GetOrCreateAsync(CancellationToken);

        // Assert
        user.Should().NotBeNull();
        var finalCount = await repository.CountAsync(CancellationToken);
        finalCount.Should().Be(initialCount + 1);
    }

    [TestMethod]
    public async Task UserGenerator_GetOrCreateAsync_ReusesSameUserOnSecondCall()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IUser>>();

        // Act
        var user1 = await generator.GetOrCreateAsync(CancellationToken);
        var user2 = await generator.GetOrCreateAsync(CancellationToken);

        // Assert
        user1.Id.Should().Be(user2.Id);
    }

    [TestMethod]
    public async Task UserGenerator_MultipleGenerateAsync_CreatesUniqueUsers()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IUser>>();

        // Act
        var user1 = await generator.CreateAsync(CancellationToken);
        var user2 = await generator.CreateAsync(CancellationToken);
        var user3 = await generator.CreateAsync(CancellationToken);

        // Assert
        user1.Id.Should().NotBe(user2.Id);
        user2.Id.Should().NotBe(user3.Id);
        user1.Id.Should().NotBe(user3.Id);
        user1.Email.Should().NotBe(user2.Email);
        user2.Email.Should().NotBe(user3.Email);
    }

    // Project Generator Tests

    [TestMethod]
    public async Task ProjectGenerator_GenerateAsync_CreatesValidProject()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();

        // Act
        var project = await generator.CreateAsync(CancellationToken);

        // Assert
        project.Should().NotBeNull();
        project.Id.Should().NotBe(Guid.Empty);
        project.Name.Should().NotBeNullOrWhiteSpace();
        project.CreatedAt.Should().NotBe(default);
        project.UpdatedAt.Should().NotBe(default);
    }

    [TestMethod]
    public async Task ProjectGenerator_CreateAsync_CreatesAndPersistsProject()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var repository = services.GetRequiredService<IRepository<IProject>>();

        // Act
        var project = await generator.CreateAsync(CancellationToken);

        // Assert
        project.Should().NotBeNull();
        project.Id.Should().NotBe(Guid.Empty);

        // Verify it was persisted
        var retrieved = await repository.GetAsync(project.Id, CancellationToken);
        retrieved.Should().NotBeNull();
        retrieved.Id.Should().Be(project.Id);
        retrieved.Name.Should().Be(project.Name);
    }

    [TestMethod]
    public async Task ProjectGenerator_GetOrCreateAsync_CreatesNewProjectFirstTime()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var repository = services.GetRequiredService<IRepository<IProject>>();
        var initialCount = await repository.CountAsync(CancellationToken);

        // Act
        var project = await generator.GetOrCreateAsync(CancellationToken);

        // Assert
        project.Should().NotBeNull();
        var finalCount = await repository.CountAsync(CancellationToken);
        finalCount.Should().Be(initialCount + 1);
    }

    [TestMethod]
    public async Task ProjectGenerator_GetOrCreateAsync_ReusesSameProjectOnSecondCall()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();

        // Act
        var project1 = await generator.GetOrCreateAsync(CancellationToken);
        var project2 = await generator.GetOrCreateAsync(CancellationToken);

        // Assert
        project1.Id.Should().Be(project2.Id);
    }

    [TestMethod]
    public async Task ProjectGenerator_MultipleGenerateAsync_CreatesUniqueProjects()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();

        // Act
        var project1 = await generator.CreateAsync(CancellationToken);
        var project2 = await generator.CreateAsync(CancellationToken);
        var project3 = await generator.CreateAsync(CancellationToken);

        // Assert
        project1.Id.Should().NotBe(project2.Id);
        project2.Id.Should().NotBe(project3.Id);
        project1.Id.Should().NotBe(project3.Id);
        project1.Name.Should().NotBe(project2.Name);
        project2.Name.Should().NotBe(project3.Name);
    }
}
