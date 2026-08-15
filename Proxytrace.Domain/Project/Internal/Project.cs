using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.User;

namespace Proxytrace.Domain.Project.Internal;

internal record Project : DomainEntity<IProject>, IProject
{
    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// Gets the system endpoint.
    /// </summary>
    public IModelEndpoint SystemEndpoint { get; }
    /// <summary>
    /// Gets the members.
    /// </summary>
    public IReadOnlyCollection<IUser> Members { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Project"/> class.
    /// </summary>
    public Project(
        string name,
        IModelEndpoint systemEndpoint,
        IReadOnlyCollection<IUser> members,
        IRepository<IProject> repository) : base(repository)
    {
        Name = name;
        SystemEndpoint = systemEndpoint;
        Members = members.ToArray();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Project"/> class.
    /// </summary>
    public Project(
        string name,
        IModelEndpoint systemEndpoint,
        IReadOnlyCollection<IUser> members,
        IDomainEntityData existing,
        IRepository<IProject> repository) : base(existing, repository)
    {
        Name = name;
        SystemEndpoint = systemEndpoint;
        Members = members.ToArray();
    }

    /// <summary>
    /// Validates.
    /// </summary>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            yield return Validation.NotNullOrWhiteSpace(Name);
        }

        foreach (var result in SystemEndpoint.Validate(validationContext))
        {
            yield return result;
        }

        foreach (var result in Members.SelectMany(m => m.Validate(validationContext)))
        {
            yield return result;
        }
    }
}
