using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Evaluator;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.TestCase;

namespace Proxytrace.Domain.TestSuite.Internal;

internal record TestSuite : DomainEntity<ITestSuite>, ITestSuite
{
    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// Gets the agent.
    /// </summary>
    public IAgent Agent { get; }
    /// <summary>
    /// Gets the evaluators.
    /// </summary>
    public IReadOnlyCollection<IEvaluator> Evaluators { get; }
    /// <summary>
    /// Gets the test cases.
    /// </summary>
    public IReadOnlyCollection<ITestCase> TestCases { get; }
    /// <summary>
    /// Gets the project.
    /// </summary>
    public IProject Project => Agent.Project;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestSuite"/> class.
    /// </summary>
    public TestSuite(
        string name,
        IAgent agent,
        IReadOnlyCollection<IEvaluator> evaluators,
        IReadOnlyCollection<ITestCase> testCases,
        IRepository<ITestSuite> repository) : base(repository)
    {
        Name = name;
        Agent = agent;
        Evaluators = evaluators.ToArray();
        TestCases = testCases.ToArray();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestSuite"/> class.
    /// </summary>
    public TestSuite(
        string name,
        IAgent agent,
        IReadOnlyCollection<IEvaluator> evaluators,
        IReadOnlyCollection<ITestCase> testCases,
        IDomainEntityData existing,
        IRepository<ITestSuite> repository) : base(existing, repository)
    {
        Name = name;
        Agent = agent;
        Evaluators = evaluators.ToArray();
        TestCases = testCases.ToArray();
    }

    /// <summary>
    /// Validates.
    /// </summary>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        if (string.IsNullOrWhiteSpace(Name))
            yield return Validation.NotNullOrWhiteSpace(Name);

        foreach (var result in Agent.Validate(validationContext))
            yield return result;

        foreach (var result in Evaluators.SelectMany(e => e.Validate(validationContext)))
            yield return result;

        foreach (var result in TestCases.SelectMany(x => x.Validate(validationContext)))
            yield return result;
    }
}
