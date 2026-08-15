namespace Proxytrace.Domain.Evaluation.Internal;

/// <summary>
/// Sentinel exception used by storage mappers to round-trip an evaluation error message verbatim
/// through the <see cref="IEvaluation.CreateErrored"/> delegate without prepending the exception
/// type name.
/// </summary>
internal sealed class StoredEvaluationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StoredEvaluationException"/> class.
    /// </summary>
    public StoredEvaluationException(string message) : base(message)
    {
    }
}
