using System.Text.RegularExpressions;
using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.Evaluator;

/// <summary>
/// Check for numeric match within a certain tolerance
/// </summary>
public interface INumericMatchEvaluator : IEvaluator
{
    public delegate INumericMatchEvaluator CreateNew(
        Regex extractionPattern,
        decimal tolerance,
        IProject project);
    
    public delegate INumericMatchEvaluator CreateExisting(
        Regex extractionPattern,
        decimal tolerance,
        IProject project,
        IDomainEntityData existing);
    
    Regex ExtractionPattern { get; }
    decimal Tolerance { get; }
}
