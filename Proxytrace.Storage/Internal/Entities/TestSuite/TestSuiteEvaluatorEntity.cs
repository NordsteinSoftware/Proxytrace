namespace Proxytrace.Storage.Internal.Entities.TestSuite;

/// <summary>
/// Join table entity for the many-to-many relationship between TestSuites and Evaluators.
/// This is a storage-only entity with no domain counterpart.
/// </summary>
internal record TestSuiteEvaluatorEntity
{
    /// <summary>
    /// Gets or sets the test suite id.
    /// </summary>
    public required Guid TestSuiteId { get; init; }
    /// <summary>
    /// Gets or sets the evaluator id.
    /// </summary>
    public required Guid EvaluatorId { get; init; }
}
