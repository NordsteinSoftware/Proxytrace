using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class TestRunValidationTests : DomainTest<Module>
{
    [TestMethod]
    public async Task CreateNew_WithValidInputs_CreatesTestRun()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var createGroup = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var createRun = services.GetRequiredService<ITestRun.CreateNew>();
        var suite = await GetOrCreate<ITestSuite>(services);
        var endpoint = await GetOrCreate<IModelEndpoint>(services);

        var group = createGroup(suite, false, null, sampleCount: 1);

        // Act
        var testRun = createRun(group, endpoint, sampleIndex: 0);

        // Assert
        testRun.Should().NotBeNull();
        testRun.Group.Should().Be(group);
        testRun.Endpoint.Should().Be(endpoint);
        testRun.TestResults.Should().BeEmpty();
        testRun.Status.Should().Be(TestRunStatus.Pending);
        testRun.Id.Should().NotBe(Guid.Empty);
        testRun.CreatedAt.Should().NotBe(default);
        testRun.UpdatedAt.Should().NotBe(default);
    }

    private async Task<ITestRun> CreateRunAsync(IServiceProvider services)
    {
        var createRun = services.GetRequiredService<ITestRun.CreateNew>();
        var runs = services.GetRequiredService<IRepository<ITestRun>>();
        var group = await GetOrCreate<ITestRunGroup>(services);
        var endpoint = await GetOrCreate<IModelEndpoint>(services);

        return await runs.AddAsync(createRun(group, endpoint, sampleIndex: 0), CancellationToken);
    }

    [TestMethod]
    public async Task SetCompleted_FromRunning_TransitionsToCompletedWithTimestamp()
    {
        IServiceProvider services = GetServices();
        var testRun = await CreateRunAsync(services);
        testRun = await testRun.SetRunning(CancellationToken);

        var updated = await testRun.SetCompleted(CancellationToken);

        updated.Status.Should().Be(TestRunStatus.Completed);
        updated.CompletedAt.Should().NotBeNull();
        updated.CompletedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task SetFailed_FromRunning_TransitionsToFailedWithTimestamp()
    {
        IServiceProvider services = GetServices();
        var testRun = await CreateRunAsync(services);
        testRun = await testRun.SetRunning(CancellationToken);

        var updated = await testRun.SetFailed(CancellationToken);

        updated.Status.Should().Be(TestRunStatus.Failed);
        updated.CompletedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task SetCompleted_IsPersisted()
    {
        IServiceProvider services = GetServices();
        var runs = services.GetRequiredService<IRepository<ITestRun>>();
        var testRun = await CreateRunAsync(services);

        await testRun.SetCompleted(CancellationToken);

        var stored = await runs.GetAsync(testRun.Id, CancellationToken);
        stored.Status.Should().Be(TestRunStatus.Completed);
    }

    [TestMethod]
    public async Task SetFailed_WhenAlreadyTerminal_Throws()
    {
        IServiceProvider services = GetServices();
        var testRun = await CreateRunAsync(services);
        testRun = await testRun.SetCancelled(CancellationToken);

        await FluentActions
            .Invoking(() => testRun.SetFailed(CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
