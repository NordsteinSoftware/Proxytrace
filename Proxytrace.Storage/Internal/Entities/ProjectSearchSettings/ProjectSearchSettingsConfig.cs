using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.ProjectSearchSettings;
using Proxytrace.Domain.Search;
using Proxytrace.Storage.Internal.Entities.Project;

namespace Proxytrace.Storage.Internal.Entities.ProjectSearchSettings;

internal class ProjectSearchSettingsConfig
    : AbstractEntityConfiguration<ProjectSearchSettingsEntity>,
      IMapper<IProjectSearchSettings, ProjectSearchSettingsEntity>
{
    private readonly IProjectSearchSettings.CreateExisting factory;
    private readonly IRepository<IProject> projects;

    public ProjectSearchSettingsConfig(
        IProjectSearchSettings.CreateExisting factory,
        IRepository<IProject> projects)
    {
        this.factory = factory;
        this.projects = projects;
    }

    public override void Configure(EntityTypeBuilder<ProjectSearchSettingsEntity> builder)
    {
        builder.HasIndex(e => e.Project).IsUnique();

        builder
            .HasOne<ProjectEntity>()
            .WithMany()
            .HasForeignKey(e => e.Project)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.IndexedKinds).HasMaxLength(256);
    }

    public async Task<IProjectSearchSettings> Map(ProjectSearchSettingsEntity stored, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetAsync(stored.Project, cancellationToken);
        var kinds = ParseKinds(stored.IndexedKinds);
        return factory(
            project: project,
            enabled: stored.Enabled,
            indexedKinds: kinds,
            autoReindexOnChange: stored.AutoReindexOnChange,
            snippetLength: stored.SnippetLength,
            existing: stored);
    }

    public Task<ProjectSearchSettingsEntity> Map(IProjectSearchSettings domain, CancellationToken cancellationToken = default)
        => new ProjectSearchSettingsEntity
        {
            Id = domain.Id,
            Project = domain.Project.Id,
            Enabled = domain.Enabled,
            IndexedKinds = SerializeKinds(domain.IndexedKinds),
            AutoReindexOnChange = domain.AutoReindexOnChange,
            SnippetLength = domain.SnippetLength,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();

    internal static string SerializeKinds(IReadOnlyCollection<SearchKind> kinds)
        => string.Join(',', kinds.Select(k => k.ToString()));

    internal static IReadOnlyCollection<SearchKind> ParseKinds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }
        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Enum.TryParse<SearchKind>(s, out var k) ? (SearchKind?)k : null)
            .Where(k => k.HasValue)
            .Select(k => k ?? throw new InvalidOperationException($"Unexpected null value when parsing SearchKind from '{raw}'"))
            .Distinct()
            .ToArray();
    }
}
