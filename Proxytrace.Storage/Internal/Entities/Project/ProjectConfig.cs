using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Proxytrace.Storage.Internal.Entities.ModelEndpoint;

namespace Proxytrace.Storage.Internal.Entities.Project;

internal class ProjectConfig : AbstractEntityConfiguration<ProjectEntity>, IMapper<IProject, ProjectEntity>
{
    private readonly IProject.CreateExisting factory;
    private readonly IRepository<IModelEndpoint> endpoints;
    private readonly IRepository<IUser> users;
    private readonly Func<StorageDbContext> contextFactory;

    public ProjectConfig(
        IProject.CreateExisting factory,
        IRepository<IModelEndpoint> endpoints,
        IRepository<IUser> users,
        Func<StorageDbContext> contextFactory)
    {
        this.factory = factory;
        this.endpoints = endpoints;
        this.users = users;
        this.contextFactory = contextFactory;
    }

    public override void Configure(EntityTypeBuilder<ProjectEntity> builder)
    {
        builder.HasIndex(e => e.Name).IsUnique();

        builder
            .HasOne<ModelEndpointEntity>()
            .WithMany()
            .HasForeignKey(e => e.SystemEndpoint)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public async Task<IProject> Map(ProjectEntity stored, CancellationToken cancellationToken = default)
    {
        var endpoint = await endpoints.GetAsync(stored.SystemEndpoint, cancellationToken);

        var memberIds = await contextFactory()
            .Set<ProjectUserEntity>()
            .AsNoTracking()
            .Where(j => j.ProjectId == stored.Id)
            .Select(j => j.UserId)
            .ToListAsync(cancellationToken);

        IReadOnlyCollection<IUser> members = memberIds.Count > 0
            ? await users.GetManyAsync(memberIds, cancellationToken: cancellationToken)
            : [];

        return factory(stored.Name, endpoint, members, stored);
    }

    public Task<ProjectEntity> Map(IProject domain, CancellationToken cancellationToken = default)
        => new ProjectEntity
        {
            Id = domain.Id,
            Name = domain.Name,
            SystemEndpoint = domain.SystemEndpoint.Id,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
            ProjectUsers = domain.Members
                .Select(u => new ProjectUserEntity { ProjectId = domain.Id, UserId = u.Id })
                .ToList()
        }.ToTaskResult();
}
