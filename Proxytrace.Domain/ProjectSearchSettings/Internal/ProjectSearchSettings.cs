using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Search;

namespace Proxytrace.Domain.ProjectSearchSettings.Internal;

internal record ProjectSearchSettings : DomainEntity<IProjectSearchSettings>, IProjectSearchSettings
{
    public const int MinSnippetLength = 20;
    public const int MaxSnippetLength = 1000;

    public IProject Project { get; }
    public bool Enabled { get; }
    public IReadOnlyCollection<SearchKind> IndexedKinds { get; }
    public bool AutoReindexOnChange { get; }
    public int SnippetLength { get; }

    public ProjectSearchSettings(
        IProject project,
        bool enabled,
        IReadOnlyCollection<SearchKind> indexedKinds,
        bool autoReindexOnChange,
        int snippetLength,
        IRepository<IProjectSearchSettings> repository) : base(repository)
    {
        Project = project;
        Enabled = enabled;
        IndexedKinds = indexedKinds.Distinct().ToArray();
        AutoReindexOnChange = autoReindexOnChange;
        SnippetLength = snippetLength;
    }

    public ProjectSearchSettings(
        IProject project,
        bool enabled,
        IReadOnlyCollection<SearchKind> indexedKinds,
        bool autoReindexOnChange,
        int snippetLength,
        IDomainEntityData existing,
        IRepository<IProjectSearchSettings> repository) : base(existing, repository)
    {
        Project = project;
        Enabled = enabled;
        IndexedKinds = indexedKinds.Distinct().ToArray();
        AutoReindexOnChange = autoReindexOnChange;
        SnippetLength = snippetLength;
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        yield return Validation.NotNull(Project);

        foreach (var result in Project.Validate(validationContext))
        {
            yield return result;
        }

        if (IndexedKinds.Count == 0)
        {
            yield return new ValidationResult(
                $"{nameof(IndexedKinds)} must contain at least one kind",
                [nameof(IndexedKinds)]);
        }

        foreach (var kind in IndexedKinds)
        {
            if (!Enum.IsDefined(kind))
            {
                yield return new ValidationResult(
                    $"{nameof(IndexedKinds)} contains undefined value '{(int)kind}'",
                    [nameof(IndexedKinds)]);
            }
        }

        if (SnippetLength < MinSnippetLength || SnippetLength > MaxSnippetLength)
        {
            yield return new ValidationResult(
                $"{nameof(SnippetLength)} must be between {MinSnippetLength} and {MaxSnippetLength}",
                [nameof(SnippetLength)]);
        }
    }
}
