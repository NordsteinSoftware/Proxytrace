using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class ProjectValidationTests : BaseTest<Module>
{
    [TestMethod]
    public async Task CreateNew_WithValidName_CreatesProject()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IProject.CreateNew>();
        var name = "Test Project";
        var endpoint = await GetEndpointAsync(services);

        // Act
        var project = factory(name, endpoint, []);

        // Assert
        project.Should().NotBeNull();
        project.Name.Should().Be(name);
        project.Id.Should().NotBe(Guid.Empty);
        project.CreatedAt.Should().NotBe(default);
        project.UpdatedAt.Should().NotBe(default);
    }

    [TestMethod]
    public async Task CreateNew_WithNullName_ThrowsValidationException()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IProject.CreateNew>();
        var endpoint = await GetEndpointAsync(services);

        // Act & Assert
        var action = () => factory.DynamicInvoke(null, endpoint, Array.Empty<IUser>());
        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_WithEmptyName_ThrowsValidationException()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IProject.CreateNew>();
        var endpoint = await GetEndpointAsync(services);

        // Act & Assert
        var action = () => factory(string.Empty, endpoint, []);
        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_WithWhitespaceName_ThrowsValidationException()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IProject.CreateNew>();
        var endpoint = await GetEndpointAsync(services);

        // Act & Assert
        var action = () => factory("   ", endpoint, []);
        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_WithTabsName_ThrowsValidationException()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IProject.CreateNew>();
        var endpoint = await GetEndpointAsync(services);

        // Act & Assert
        var action = () => factory("\t\t\t", endpoint, []);
        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateExisting_WithValidData_CreatesProject()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var createExisting = services.GetRequiredService<IProject.CreateExisting>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var existingProject = await generator.CreateAsync(CancellationToken);

        // Act
        var project = createExisting(existingProject.Name, existingProject.SystemEndpoint, [], existingProject);

        // Assert
        project.Should().NotBeNull();
        project.Id.Should().Be(existingProject.Id);
        project.Name.Should().Be(existingProject.Name);
        project.CreatedAt.Should().Be(existingProject.CreatedAt);
        project.UpdatedAt.Should().Be(existingProject.UpdatedAt);
    }

    [TestMethod]
    public async Task CreateExisting_WithInvalidName_ThrowsValidationException()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var createExisting = services.GetRequiredService<IProject.CreateExisting>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var existingProject = await generator.CreateAsync(CancellationToken);

        // Act & Assert
        var action = () => createExisting(string.Empty, existingProject.SystemEndpoint, [], existingProject);
        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task Id_IsUniqueForEachNewProject()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IProject.CreateNew>();
        var endpoint = await GetEndpointAsync(services);

        // Act
        var project1 = factory("Project 1", endpoint, []);
        var project2 = factory("Project 2", endpoint, []);

        // Assert
        project1.Id.Should().NotBe(project2.Id);
    }

    [TestMethod]
    public async Task CreateNew_WithLongName_CreatesProject()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IProject.CreateNew>();
        var endpoint = await GetEndpointAsync(services);

        // Act
        var project = factory(new string('A', 1000), endpoint, []);

        // Assert
        project.Should().NotBeNull();
        project.Name.Should().Be(new string('A', 1000));
    }

    [TestMethod]
    public async Task CreateNew_WithSpecialCharactersInName_CreatesProject()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IProject.CreateNew>();
        var endpoint = await GetEndpointAsync(services);

        // Act
        var project = factory("Project @#$% 123 !&*()", endpoint, []);

        // Assert
        project.Should().NotBeNull();
        project.Name.Should().Be("Project @#$% 123 !&*()");
    }

    [TestMethod]
    public async Task CreateNew_WithUnicodeCharactersInName_CreatesProject()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IProject.CreateNew>();
        var endpoint = await GetEndpointAsync(services);

        // Act
        var project = factory("项目 José Müller", endpoint, []);

        // Assert
        project.Should().NotBeNull();
        project.Name.Should().Be("项目 José Müller");
    }

    [TestMethod]
    public async Task Project_IsImmutable()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var project = await generator.CreateAsync(CancellationToken);

        // Act & Assert
        var nameProperty = project.GetType().GetProperty("Name");
        nameProperty.Should().NotBeNull();
        nameProperty.SetMethod.Should().BeNull();
    }

    private async Task<IModelEndpoint> GetEndpointAsync(IServiceProvider services)
        => await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
}
