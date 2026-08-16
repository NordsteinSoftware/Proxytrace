using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.Model.Internal;

internal record Model : DomainEntity<IModel>, IModel
{
    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Model"/> class.
    /// </summary>
    public Model(string name, IRepository<IModel> repository) : base(repository)
    {
        Name = name;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Model"/> class.
    /// </summary>
    public Model(string name, IDomainEntityData existing, IRepository<IModel> repository) : base(existing, repository)
    {
        Name = name;
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
        yield return Validation.NotNullOrWhiteSpace(Name);
    }
}
