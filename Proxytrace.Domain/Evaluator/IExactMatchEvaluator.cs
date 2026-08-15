using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.Evaluator;

/// <summary>
/// Represents a exact match evaluator.
/// </summary>
public interface IExactMatchEvaluator : IEvaluator
{
    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate IExactMatchEvaluator CreateNew(IProject project);
    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate IExactMatchEvaluator CreateExisting(
        IProject project,
        IDomainEntityData existing);
}
