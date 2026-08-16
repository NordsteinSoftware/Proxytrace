using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.Evaluator;

/// <summary>
/// Checks whether the output matches the given Json schema
/// </summary>
public interface IJsonSchemaMatchEvaluator : IEvaluator
{
    string JsonSchema { get; }
    
    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate IJsonSchemaMatchEvaluator CreateNew(
        string jsonSchema,
        IProject project);
    
    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate IJsonSchemaMatchEvaluator CreateExisting(
        string jsonSchema,
        IProject project,
        IDomainEntityData existing);
}
