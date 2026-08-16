using Proxytrace.Domain.Agent;

namespace Proxytrace.Domain.Evaluator;

/// <summary>
/// Represents a agentic evaluator.
/// </summary>
public interface IAgenticEvaluator : IEvaluator
{
    /// <summary>
    /// Gets the agent.
    /// </summary>
    public IAgent Agent { get; }
    
    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate IAgenticEvaluator CreateNew(IAgent agent);
    
    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate IAgenticEvaluator CreateExisting(
        IAgent agent,
        IDomainEntityData existing);
}
