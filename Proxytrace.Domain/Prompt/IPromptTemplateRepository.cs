using Nordstein.Core.AI.Prompts;
namespace Proxytrace.Domain.Prompt;

/// <summary>
/// Repository for <see cref="IPromptTemplate"/>
/// </summary>
public interface IPromptTemplateRepository
{
    /// <summary>
    /// Gets a <see cref="IPromptTemplate"/> by its <paramref name="name"/>.
    /// If it does not exist, a <see cref="PromptNotFoundException"/> is thrown
    /// </summary>
    Task<IPromptTemplate> GetAsync(string name, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Tries to find a <see cref="IPromptTemplate"/> by its <paramref name="name"/>.
    /// </summary>
    Task<IPromptTemplate?> FindAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// The exception that is thrown when a prompt not found error occurs.
/// </summary>
public class PromptNotFoundException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PromptNotFoundException"/> class.
    /// </summary>
    public PromptNotFoundException(string name) 
        : base($"Prompt with name '{name}' not found.") { }
}
