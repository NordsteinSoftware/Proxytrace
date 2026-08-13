using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;

namespace Proxytrace.Domain.Message;

/// <summary>
/// Request to call a tool
/// </summary>
public sealed record ToolRequest : IDomainObject
{
    /// <summary>
    /// The id of the tool request
    /// </summary>
    public string Id { get; }
    
    /// <summary>
    /// The name of the tool to call
    /// </summary>
    public string Name { get; }
    
    /// <summary>
    /// The arguments to pass to the tool
    /// </summary>
    public string Arguments { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolRequest"/> class with the specified id, name, and arguments.
    /// </summary>
    /// <param name="id">The id of the tool request.</param>
    /// <param name="name">The name of the tool to call.</param>
    /// <param name="arguments">The arguments to pass to the tool.</param>
    public ToolRequest(string id, string name, string arguments)
    {
        Id = id;
        Name = name;
        Arguments = arguments;
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        yield return Validation.NotNullOrWhiteSpace(Id);
        yield return Validation.NotNullOrWhiteSpace(Name);
    }
}