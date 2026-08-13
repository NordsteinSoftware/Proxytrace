using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;

namespace Proxytrace.Domain.Tools;

/// <summary>
/// Describes an available AI tool
/// </summary>
public sealed record ToolSpecification : IDomainObject
{
    /// <summary>
    /// The name of the tool. Must be unique within the agent's toolset.
    /// </summary>
    public string Name { get; }
    
    /// <summary>
    /// The description of the tool. This will be shown to the agent when deciding which tool to use.
    /// </summary>
    public string Description { get; }
    
    /// <summary>
    /// The arguments the tool accepts
    /// </summary>
    public ToolArguments Arguments { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolSpecification"/> class with the specified name, description, and arguments.
    /// </summary>
    public ToolSpecification(string name, string description, ToolArguments arguments)
    {
        Name = name;
        Description = description;
        Arguments = arguments;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        yield return Validation.NotNullOrWhiteSpace(Name);
        yield return Validation.NotNullOrWhiteSpace(Description);
        foreach (var result in Arguments.Validate(validationContext))
        {
            yield return result;
        }
    }
}