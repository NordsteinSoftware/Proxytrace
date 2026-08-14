using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.Evaluator;

public interface IExactMatchEvaluator : IEvaluator
{
    public delegate IExactMatchEvaluator CreateNew(IProject project);
    public delegate IExactMatchEvaluator CreateExisting(
        IProject project,
        IDomainEntityData existing);
}
