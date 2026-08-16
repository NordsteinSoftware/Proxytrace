using Proxytrace.Domain.TestCase;

namespace Proxytrace.Domain.TestResult;

/// <summary>
/// Generates test result instances.
/// </summary>
public interface ITestResultGenerator : IDomainEntityGenerator<ITestResult>
{
    Task<ITestResult> CreateAsync(ITestCase testCase, CancellationToken cancellationToken = default);
}
