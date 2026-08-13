using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.Internal;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.TestCase;

namespace Proxytrace.Domain.TestSuite.Internal;

internal record TestSuite : DomainEntity<ITestSuite>, ITestSuite
{
    public string Name { get; }
    public IAgent Agent { get; }
    public IReadOnlyCollection<IEvaluator> Evaluators { get; }
    public IReadOnlyCollection<ITestCase> TestCases { get; }
    public IProject Project => Agent.Project;

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
