using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class UserRepositoryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task FindByEmail_WithExistingUser_ReturnsUser()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IUserRepository>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IUser>>();
        var user = await generator.CreateAsync(CancellationToken);

        // Act
        var foundUser = await repository.FindByEmailAsync(user.Email, CancellationToken);

        // Assert
        foundUser.Should().NotBeNull();
        foundUser.Id.Should().Be(user.Id);
        foundUser.Email.Should().Be(user.Email);
    }

    [TestMethod]
    public async Task FindByEmail_WithNonExistingUser_ReturnsNull()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IUserRepository>();
        var nonExistentEmail = "nonexistent_" + Guid.NewGuid() + "@example.com";

        // Act
        var foundUser = await repository.FindByEmailAsync(nonExistentEmail, CancellationToken);

        // Assert
        foundUser.Should().BeNull();
    }

    [TestMethod]
    public async Task FindByEmail_WithEmptyDatabase_ReturnsNull()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IUserRepository>();

        // Act
        var foundUser = await repository.FindByEmailAsync("any@example.com", CancellationToken);

        // Assert
        foundUser.Should().BeNull();
    }

    [TestMethod]
    public async Task FindByEmail_WithExactMatch_ReturnsCorrectUser()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IUserRepository>();
        var factory = services.GetRequiredService<IUser.CreateNew>();

        var user1 = factory("john.doe@example.com", "iss|" + Guid.NewGuid(), null, UserRole.Member);
        var user2 = factory("jane.doe@example.com", "iss|" + Guid.NewGuid(), null, UserRole.Member);
        var user3 = factory("john.smith@example.com", "iss|" + Guid.NewGuid(), null, UserRole.Member);

        await repository.AddAsync(user1, CancellationToken);
        await repository.AddAsync(user2, CancellationToken);
        await repository.AddAsync(user3, CancellationToken);

        // Act
        var foundUser = await repository.FindByEmailAsync("jane.doe@example.com", CancellationToken);

        // Assert
        foundUser.Should().NotBeNull();
        foundUser.Id.Should().Be(user2.Id);
        foundUser.Email.Should().Be("jane.doe@example.com");
    }

    [TestMethod]
    public async Task FindByEmail_AfterUserRemoval_ReturnsNull()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IUserRepository>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IUser>>();

        var user = await generator.CreateAsync(CancellationToken);
        var userEmail = user.Email;

        // Remove the user
        await repository.RemoveAsync(user.Id, CancellationToken);

        // Act
        var foundUser = await repository.FindByEmailAsync(userEmail, CancellationToken);

        // Assert
        foundUser.Should().BeNull();
    }

    [TestMethod]
    public async Task FindByEmail_WithMultipleUsersWithDifferentEmails_ReturnsCorrectUser()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IUserRepository>();
        var factory = services.GetRequiredService<IUser.CreateNew>();

        var users = new[]
        {
            factory("alice@example.com", "iss|" + Guid.NewGuid(), null, UserRole.Member),
            factory("bob@example.com", "iss|" + Guid.NewGuid(), null, UserRole.Member),
            factory("charlie@example.com", "iss|" + Guid.NewGuid(), null, UserRole.Member),
            factory("david@example.com", "iss|" + Guid.NewGuid(), null, UserRole.Member),
            factory("eve@example.com", "iss|" + Guid.NewGuid(), null, UserRole.Member)
        };

        foreach (var user in users)
        {
            await repository.AddAsync(user, CancellationToken);
        }

        // Act
        var foundAlice = await repository.FindByEmailAsync("alice@example.com", CancellationToken);
        var foundCharlie = await repository.FindByEmailAsync("charlie@example.com", CancellationToken);
        var foundEve = await repository.FindByEmailAsync("eve@example.com", CancellationToken);

        // Assert
        foundAlice.Should().NotBeNull();
        foundAlice.Email.Should().Be("alice@example.com");

        foundCharlie.Should().NotBeNull();
        foundCharlie.Email.Should().Be("charlie@example.com");

        foundEve.Should().NotBeNull();
        foundEve.Email.Should().Be("eve@example.com");
    }

    [TestMethod]
    public async Task FindByEmail_CalledMultipleTimes_ReturnsSameUser()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IUserRepository>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IUser>>();
        var user = await generator.CreateAsync(CancellationToken);

        // Act
        var found1 = await repository.FindByEmailAsync(user.Email, CancellationToken);
        var found2 = await repository.FindByEmailAsync(user.Email, CancellationToken);
        var found3 = await repository.FindByEmailAsync(user.Email, CancellationToken);

        // Assert
        found1.Should().NotBeNull();
        found2.Should().NotBeNull();
        found3.Should().NotBeNull();
        found1.Id.Should().Be(user.Id);
        found2.Id.Should().Be(user.Id);
        found3.Id.Should().Be(user.Id);
    }

    [TestMethod]
    public async Task FindByEmail_ReturnsUserWithAllProperties()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IUserRepository>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IUser>>();
        var user = await generator.CreateAsync(CancellationToken);

        // Act
        var foundUser = await repository.FindByEmailAsync(user.Email, CancellationToken);

        // Assert
        foundUser.Should().NotBeNull();
        foundUser.Should().BeEquivalentTo(user);
    }
}
