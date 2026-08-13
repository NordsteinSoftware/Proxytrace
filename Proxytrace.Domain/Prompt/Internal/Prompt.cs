using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Contracts;
using Nordstein.Core.Common.Validation;

namespace Proxytrace.Domain.Prompt.Internal;

/// <inheritdoc />
internal record Prompt : IPrompt
{
    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// The rendered prompt string.
    /// </summary>
    private readonly string promptString;

    public Prompt(
        string name,
        string promptString)
    {
        // Routes through the framework guard: ArgumentNullException for null, ArgumentException for
        // a non-null but empty/whitespace value (the previous ArgumentNullException was the wrong
        // type for whitespace).
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptString);


        Name = name;
        this.promptString = promptString;
    }

    /// <inheritdoc />
    public string ToPromptString() 
        => promptString;

    /// <inheritdoc />
    [Pure]
    public IPrompt Append(IPrompt other) 
        => new Prompt(
            name: $"{Name}_{other.Name}",
            promptString: string.Join(Environment.NewLine, ToPromptString(), other.ToPromptString()));

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        yield return Validation.NotNullOrWhiteSpace(Name);
        yield return Validation.NotNullOrWhiteSpace(promptString);
    }
}
