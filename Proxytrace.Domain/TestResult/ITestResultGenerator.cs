using Proxytrace.Domain.TestCase;

namespace Proxytrace.Domain.TestResult;

public interface ITestResultGenerator : IDomainEntityGenerator<ITestResult>
{
    Task<ITestResult> CreateAsync(ITestCase testCase, CancellationToken cancellationToken = default);
}
