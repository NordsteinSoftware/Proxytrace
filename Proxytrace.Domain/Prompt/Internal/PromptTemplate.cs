using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Nordstein.Core.Common.Validation;

namespace Proxytrace.Domain.Prompt.Internal;

/// <inheritdoc />
internal record PromptTemplate : IPromptTemplate
{
    private static readonly Regex VariableRegex = new(@"\{\{([a-zA-Z0-9_-]+)\}\}", RegexOptions.Compiled);

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Template { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<string> Variables { get; }

    public PromptTemplate(
        string name, 
        string template)
    {
        Name = name;
        Template = template;
        Variables = ExtractVariables(template);
    }

    /// <inheritdoc />
    public IPrompt Render(IReadOnlyDictionary<string, string>? values = null)
    {
        this.Validate();
        string renderedTemplate = Template;
        values ??= new  Dictionary<string, string>();

        // Replace each variable with its value
        foreach (string variable in Variables)
        {
            if (!values.TryGetValue(variable, out string? value))
            {
                throw new ArgumentException($"Value for variable '{variable}' was not provided.");
            }
            renderedTemplate = renderedTemplate.Replace($"{{{{{variable}}}}}", value);
        }

        var prompt = new Prompt(Name, renderedTemplate);
        prompt.Validate();
        return prompt;
    }

    private IReadOnlyCollection<string> ExtractVariables(string template)
    {
        HashSet<string> variables = [];
        foreach (Match match in VariableRegex.Matches(template))
        {
            string variableName = match.Groups[1].Value;
            variables.Add(variableName);
        }
        return variables;
    }

    /// <inheritdoc />
    public virtual bool Equals(PromptTemplate? other)
        => other is not null
           && Name == other.Name
           && Template == other.Template;

    /// <inheritdoc />
    public override int GetHashCode()
        // Name + Template fully determine the template (Variables is derived from Template), so
        // they alone define value equality. The synthesized record members would compare the
        // Variables HashSet by reference, making two identically-constructed templates unequal —
        // which silently disabled every "has the prompt actually changed?" dedup check
        // (e.g. Agent.ChangeSystemMessage).
        => HashCode.Combine(Name, Template);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        yield return Validation.NotNullOrWhiteSpace(Name);
        yield return Validation.NotNullOrWhiteSpace(Template);
        foreach (var variable in Variables)
        {
            yield return Validation.NotNullOrWhiteSpace(variable);
            // Require at least one letter (rejects purely numeric/underscore names like {{1}});
            // single-letter names such as {{x}} are valid and must pass.
            var letterCount = variable.Count(char.IsLetter);
            yield return Validation.GreaterThan(letterCount, 0);
        }
    }
}