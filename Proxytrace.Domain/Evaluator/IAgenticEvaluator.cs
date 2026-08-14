using Proxytrace.Domain.Agent;

namespace Proxytrace.Domain.Evaluator;

public interface IAgenticEvaluator : IEvaluator
{
    public IAgent Agent { get; }
    
    public delegate IAgenticEvaluator CreateNew(IAgent agent);
    
    public delegate IAgenticEvaluator CreateExisting(
        IAgent agent,
        IDomainEntityData existing);
}
