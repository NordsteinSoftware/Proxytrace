using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class ProjectRepositoryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task AddAsync_WithMembers_PersistsAll()
    {
        IServiceProvider services = GetServices();
        var (repository, projectFactory, endpoint, users) = await SetupAsync(services, memberCount: 3);

        var project = projectFactory(name: "Project A", systemEndpoint: endpoint, members: users);
        await repository.AddAsync(project, CancellationToken);

        var retrieved = await repository.GetAsync(project.Id, CancellationToken);
        retrieved.Members.Select(m => m.Id).Should().BeEquivalentTo(users.Select(u => u.Id));
    }

    [TestMethod]
    public async Task GetAsync_WithoutMembers_ReturnsEmptyMembers()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var repository = services.GetRequiredService<IRepository<IProject>>();
        var project = await generator.CreateAsync(CancellationToken);

        var retrieved = await repository.GetAsync(project.Id, CancellationToken);

        retrieved.Members.Should().BeEmpty();
    }

    [TestMethod]
    public async Task UpdateAsync_AddingMember_PersistsRelationship()
    {
        IServiceProvider services = GetServices();
        var (repository, projectFactory, endpoint, users) = await SetupAsync(services, memberCount: 2);
        var createExisting = services.GetRequiredService<IProject.CreateExisting>();

        var project = projectFactory("Add member", endpoint, [users[0]]);
        var saved = await repository.AddAsync(project, CancellationToken);

        var updated = createExisting(saved.Name, saved.SystemEndpoint, [users[0], users[1]], saved);
        await repository.UpdateAsync(updated, CancellationToken);

        var retrieved = await repository.GetAsync(saved.Id, CancellationToken);
        retrieved.Members.Should().HaveCount(2);
        retrieved.Members.Select(m => m.Id).Should().BeEquivalentTo([users[0].Id, users[1].Id]);
    }

    [TestMethod]
    public async Task UpdateAsync_RemovingMember_PersistsRelationship()
    {
        IServiceProvider services = GetServices();
        var (repository, projectFactory, endpoint, users) = await SetupAsync(services, memberCount: 2);
        var createExisting = services.GetRequiredService<IProject.CreateExisting>();

        var project = projectFactory("Remove member", endpoint, users);
        var saved = await repository.AddAsync(project, CancellationToken);

        var updated = createExisting(saved.Name, saved.SystemEndpoint, [users[0]], saved);
        await repository.UpdateAsync(updated, CancellationToken);

        var retrieved = await repository.GetAsync(saved.Id, CancellationToken);
        retrieved.Members.Should().HaveCount(1);
        retrieved.Members.Single().Id.Should().Be(users[0].Id);
    }

    [TestMethod]
    public async Task RemoveAsync_Project_CascadesJunctionRows()
    {
        IServiceProvider services = GetServices();
        var (repository, projectFactory, endpoint, users) = await SetupAsync(services, memberCount: 1);
        var userRepository = services.GetRequiredService<IRepository<IUser>>();

        var project = projectFactory("Cascade project", endpoint, users);
        var saved = await repository.AddAsync(project, CancellationToken);

        await repository.RemoveAsync(saved.Id, CancellationToken);

        var stillExists = await userRepository.ContainsAsync(users[0].Id, CancellationToken);
        stillExists.Should().BeTrue();
    }

    [TestMethod]
    public async Task FindBySlugAsync_MatchesDerivedSlug()
    {
        IServiceProvider services = GetServices();
        var (repository, projectFactory, endpoint, _) = await SetupAsync(services, memberCount: 0);
        var projectRepository = services.GetRequiredService<IProjectRepository>();

        var project = projectFactory("Showcase Project", endpoint, []);
        var saved = await repository.AddAsync(project, CancellationToken);

        var found = await projectRepository.FindBySlugAsync("showcase-project", CancellationToken);

        found.Should().NotBeNull();
        found.Id.Should().Be(saved.Id);
    }

    [TestMethod]
    public async Task FindBySlugAsync_MixedCaseSlug_MatchesCaseInsensitively()
    {
        IServiceProvider services = GetServices();
        var (repository, projectFactory, endpoint, _) = await SetupAsync(services, memberCount: 0);
        var projectRepository = services.GetRequiredService<IProjectRepository>();

        var project = projectFactory("Showcase Project", endpoint, []);
        var saved = await repository.AddAsync(project, CancellationToken);

        // A request-path segment ("/Showcase-Project/openai/v1") keeps its original casing; it must
        // still match the canonical lower-cased slug, not just the already-lower-cased form.
        var found = await projectRepository.FindBySlugAsync("Showcase-Project", CancellationToken);

        found.Should().NotBeNull();
        found.Id.Should().Be(saved.Id);
    }

    [TestMethod]
    public async Task FindBySlugAsync_UnknownSlug_ReturnsNull()
    {
        IServiceProvider services = GetServices();
        var projectRepository = services.GetRequiredService<IProjectRepository>();

        var found = await projectRepository.FindBySlugAsync("does-not-exist", CancellationToken);

        found.Should().BeNull();
    }

    [TestMethod]
    public async Task GetByMemberAsync_ReturnsOnlyProjectsTheUserBelongsTo()
    {
        IServiceProvider services = GetServices();
        var (repository, projectFactory, endpoint, users) = await SetupAsync(services, memberCount: 2);
        var projectRepository = services.GetRequiredService<IProjectRepository>();

        var member = users[0];
        var withMember = await repository.AddAsync(projectFactory("With member", endpoint, [member]), CancellationToken);
        await repository.AddAsync(projectFactory("Without member", endpoint, [users[1]]), CancellationToken);

        var result = await projectRepository.GetByMemberAsync(member.Id, CancellationToken);

        result.Select(p => p.Id).Should().ContainSingle().Which.Should().Be(withMember.Id);
    }

    [TestMethod]
    public async Task GetByMemberAsync_UserWithoutProjects_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var projectRepository = services.GetRequiredService<IProjectRepository>();

        var result = await projectRepository.GetByMemberAsync(Guid.NewGuid(), CancellationToken);

        result.Should().BeEmpty();
    }

    private async Task<(IRepository<IProject> repository,
        IProject.CreateNew projectFactory,
        IModelEndpoint endpoint,
        IUser[] users)> SetupAsync(IServiceProvider services, int memberCount)
    {
        var repository = services.GetRequiredService<IRepository<IProject>>();
        var projectFactory = services.GetRequiredService<IProject.CreateNew>();
        var endpointGenerator = services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>();
        var userGenerator = services.GetRequiredService<IDomainEntityGenerator<IUser>>();

        var endpoint = await endpointGenerator.GetOrCreateAsync(CancellationToken);
        var users = new IUser[memberCount];
        for (int i = 0; i < memberCount; i++)
        {
            users[i] = await userGenerator.CreateAsync(CancellationToken);
        }
        return (repository, projectFactory, endpoint, users);
    }
}
