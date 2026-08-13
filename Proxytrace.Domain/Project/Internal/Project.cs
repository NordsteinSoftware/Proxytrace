using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Internal;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.User;

namespace Proxytrace.Domain.Project.Internal;

internal record Project : DomainEntity<IProject>, IProject
{
    public string Name { get; }
    public IModelEndpoint SystemEndpoint { get; }
    public IReadOnlyCollection<IUser> Members { get; }

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
