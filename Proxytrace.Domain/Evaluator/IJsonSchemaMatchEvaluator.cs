using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.Evaluator;

/// <summary>
/// Checks whether the output matches the given Json schema
/// </summary>
public interface IJsonSchemaMatchEvaluator : IEvaluator
{
    string JsonSchema { get; }
    
    public delegate IJsonSchemaMatchEvaluator CreateNew(
        string jsonSchema,
        IProject project);
    
    public delegate IJsonSchemaMatchEvaluator CreateExisting(
        string jsonSchema,
        IProject project,
        IDomainEntityData existing);
}
