using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestSuite;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class OptimizationTheoryValidationTests : DomainTest<Module>
{
    [TestMethod]
    public async Task CreateNew_ProducesProposedTheory()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<ISystemPromptTheory>>();

        ISystemPromptTheory theory = await generator.GenerateAsync(CancellationToken);

        theory.Id.Should().NotBe(Guid.Empty);
        theory.Status.Should().Be(TheoryStatus.Proposed);
        theory.Source.Should().Be(TheorySource.Optimizer);
        theory.Kind.Should().Be(ProposalKind.SystemPrompt);
        theory.ResultingProposalId.Should().BeNull();
        theory.ContentHash.Should().NotBeNullOrWhiteSpace();
        theory.ProposedSystemMessage.Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public async Task SetValidating_FromProposed_TransitionsToValidating()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);

        var updated = await theory.SetValidating(CancellationToken);

        updated.Status.Should().Be(TheoryStatus.Validating);

        var repo = services.GetRequiredService<IRepository<IOptimizationTheory>>();
        var reloaded = await repo.GetAsync(theory.Id, CancellationToken);
        reloaded.Status.Should().Be(TheoryStatus.Validating);
    }

    [TestMethod]
    public async Task SetValidated_FromValidating_RecordsResultingProposalAndMetrics()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);
        var validating = await theory.SetValidating(CancellationToken);
        var proposalId = Guid.NewGuid();
        var abTestRunId = Guid.NewGuid();

        var validated = await validating.SetValidated(proposalId, 0.5, 0.7, 0.02, abTestRunId, CancellationToken);

        validated.Status.Should().Be(TheoryStatus.Validated);
        validated.ResultingProposalId.Should().Be(proposalId);
        validated.BaselinePassRate.Should().Be(0.5);
        validated.ProjectedPassRate.Should().Be(0.7);
        validated.PValue.Should().Be(0.02);
        validated.ABTestRunId.Should().Be(abTestRunId);

        var repo = services.GetRequiredService<IRepository<IOptimizationTheory>>();
        var reloaded = await repo.GetAsync(theory.Id, CancellationToken);
        reloaded.ResultingProposalId.Should().Be(proposalId);
        reloaded.BaselinePassRate.Should().Be(0.5);
        reloaded.ProjectedPassRate.Should().Be(0.7);
        reloaded.PValue.Should().Be(0.02);
        reloaded.ABTestRunId.Should().Be(abTestRunId);
    }

    [TestMethod]
    public async Task SetInvalidated_FromValidating_TransitionsToInvalidatedAndRecordsMetrics()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);
        var validating = await theory.SetValidating(CancellationToken);
        var abTestRunId = Guid.NewGuid();

        var invalidated = await validating.SetInvalidated(0.6, 0.6, 0.41, abTestRunId, CancellationToken);

        invalidated.Status.Should().Be(TheoryStatus.Invalidated);
        invalidated.BaselinePassRate.Should().Be(0.6);
        invalidated.ProjectedPassRate.Should().Be(0.6);
        invalidated.PValue.Should().Be(0.41);
        invalidated.ABTestRunId.Should().Be(abTestRunId);

        var repo = services.GetRequiredService<IRepository<IOptimizationTheory>>();
        var reloaded = await repo.GetAsync(theory.Id, CancellationToken);
        reloaded.ABTestRunId.Should().Be(abTestRunId);
    }

    [TestMethod]
    public async Task AttachAbTestRun_WhileValidating_RecordsRunIdAndPersists()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);
        var validating = await theory.SetValidating(CancellationToken);
        var abTestRunId = Guid.NewGuid();

        var attached = await validating.AttachAbTestRun(abTestRunId, CancellationToken);

        attached.Status.Should().Be(TheoryStatus.Validating);
        attached.ABTestRunId.Should().Be(abTestRunId);

        var repo = services.GetRequiredService<IRepository<IOptimizationTheory>>();
        var reloaded = await repo.GetAsync(theory.Id, CancellationToken);
        reloaded.ABTestRunId.Should().Be(abTestRunId);
    }

    [TestMethod]
    public async Task AttachAbTestRun_FromProposed_Throws()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);

        await FluentActions
            .Invoking(() => theory.AttachAbTestRun(Guid.NewGuid(), CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task ResetToProposed_FromInvalidated_ClearsMetricsAndPersists()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);
        var validating = await theory.SetValidating(CancellationToken);
        var invalidated = await validating.SetInvalidated(0.6, 0.6, 0.41, Guid.NewGuid(), CancellationToken);

        var reset = await invalidated.ResetToProposed(CancellationToken);

        reset.Status.Should().Be(TheoryStatus.Proposed);
        reset.ResultingProposalId.Should().BeNull();
        reset.BaselinePassRate.Should().BeNull();
        reset.ProjectedPassRate.Should().BeNull();
        reset.PValue.Should().BeNull();
        reset.ABTestRunId.Should().BeNull();

        var repo = services.GetRequiredService<IRepository<IOptimizationTheory>>();
        var reloaded = await repo.GetAsync(theory.Id, CancellationToken);
        reloaded.Status.Should().Be(TheoryStatus.Proposed);
        reloaded.ABTestRunId.Should().BeNull();
    }

    [TestMethod]
    public async Task ResetToProposed_FromValidated_ClearsProposalReference()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);
        var validating = await theory.SetValidating(CancellationToken);
        var validated = await validating.SetValidated(Guid.NewGuid(), 0.5, 0.7, 0.02, Guid.NewGuid(), CancellationToken);

        var reset = await validated.ResetToProposed(CancellationToken);

        reset.Status.Should().Be(TheoryStatus.Proposed);
        reset.ResultingProposalId.Should().BeNull();
    }

    [TestMethod]
    public async Task ResetToProposed_FromProposed_Throws()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);

        await FluentActions
            .Invoking(() => theory.ResetToProposed(CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task ResetToProposed_FromValidating_Throws()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);
        var validating = await theory.SetValidating(CancellationToken);

        await FluentActions
            .Invoking(() => validating.ResetToProposed(CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task SetValidated_FromProposed_Throws()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);

        await FluentActions
            .Invoking(() => theory.SetValidated(Guid.NewGuid(), null, null, null, null, CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task SetValidating_WhenNotProposed_Throws()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);
        var validating = await theory.SetValidating(CancellationToken);

        await FluentActions
            .Invoking(() => validating.SetValidating(CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task ContentHash_MatchesEquivalentProposal()
    {
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var suite = await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().CreateAsync(CancellationToken);
        var abRun = await services.GetRequiredService<IDomainEntityGenerator<ITestRun>>().CreateAsync(CancellationToken);

        const string message = "You are a precise and structured assistant.";

        var theoryFactory = services.GetRequiredService<ISystemPromptTheory.CreateNew>();
        var proposalFactory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();

        var theory = theoryFactory(agent, suite, TheorySource.User, Priority.Medium, "rationale", message, []);
        var proposal = proposalFactory(agent, Priority.Medium, "rationale", message, null, null, [], abRun);

        theory.ContentHash.Should().Be(proposal.ContentHash);
    }

    [TestMethod]
    public async Task Reject_FromProposed_TransitionsToInvalidatedWithoutMetrics()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);

        var rejected = await theory.Reject(CancellationToken);

        rejected.Status.Should().Be(TheoryStatus.Invalidated);
        rejected.BaselinePassRate.Should().BeNull();
        rejected.ProjectedPassRate.Should().BeNull();
        rejected.PValue.Should().BeNull();
        rejected.ABTestRunId.Should().BeNull();

        var repo = services.GetRequiredService<IRepository<IOptimizationTheory>>();
        var reloaded = await repo.GetAsync(theory.Id, CancellationToken);
        reloaded.Status.Should().Be(TheoryStatus.Invalidated);
    }

    [TestMethod]
    public async Task Reject_FromValidating_TransitionsToInvalidatedPreservingAbRun()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);
        var validating = await theory.SetValidating(CancellationToken);
        var abTestRunId = Guid.NewGuid();
        var attached = await validating.AttachAbTestRun(abTestRunId, CancellationToken);

        var rejected = await attached.Reject(CancellationToken);

        rejected.Status.Should().Be(TheoryStatus.Invalidated);
        rejected.ABTestRunId.Should().Be(abTestRunId);
        rejected.PValue.Should().BeNull();

        var repo = services.GetRequiredService<IRepository<IOptimizationTheory>>();
        var reloaded = await repo.GetAsync(theory.Id, CancellationToken);
        reloaded.Status.Should().Be(TheoryStatus.Invalidated);
        reloaded.ABTestRunId.Should().Be(abTestRunId);
    }

    [TestMethod]
    public async Task Reject_FromValidated_Throws()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);
        var validating = await theory.SetValidating(CancellationToken);
        var validated = await validating.SetValidated(Guid.NewGuid(), 0.5, 0.7, 0.02, Guid.NewGuid(), CancellationToken);

        await FluentActions
            .Invoking(() => validated.Reject(CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task Reject_FromInvalidated_Throws()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);
        var validating = await theory.SetValidating(CancellationToken);
        var invalidated = await validating.SetInvalidated(0.6, 0.6, 0.41, Guid.NewGuid(), CancellationToken);

        await FluentActions
            .Invoking(() => invalidated.Reject(CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task SetFailed_FromValidating_TransitionsToFailedWithoutMetricsPreservingAbRun()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);
        var validating = await theory.SetValidating(CancellationToken);
        var abTestRunId = Guid.NewGuid();
        var attached = await validating.AttachAbTestRun(abTestRunId, CancellationToken);

        var failed = await attached.SetFailed(abTestRunId, CancellationToken);

        failed.Status.Should().Be(TheoryStatus.Failed);
        failed.BaselinePassRate.Should().BeNull();
        failed.ProjectedPassRate.Should().BeNull();
        failed.PValue.Should().BeNull();
        failed.ABTestRunId.Should().Be(abTestRunId);

        var repo = services.GetRequiredService<IRepository<IOptimizationTheory>>();
        var reloaded = await repo.GetAsync(theory.Id, CancellationToken);
        reloaded.Status.Should().Be(TheoryStatus.Failed);
        reloaded.ABTestRunId.Should().Be(abTestRunId);
    }

    [TestMethod]
    public async Task SetFailed_FromProposed_Throws()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);

        await FluentActions
            .Invoking(() => theory.SetFailed(null, CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task SetFailed_FromInvalidated_Throws()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);
        var validating = await theory.SetValidating(CancellationToken);
        var invalidated = await validating.SetInvalidated(0.6, 0.6, 0.41, Guid.NewGuid(), CancellationToken);

        await FluentActions
            .Invoking(() => invalidated.SetFailed(null, CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task ResetToProposed_FromFailed_ClearsRunLinkAndPersists()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);
        var validating = await theory.SetValidating(CancellationToken);
        var failed = await validating.SetFailed(Guid.NewGuid(), CancellationToken);

        var reset = await failed.ResetToProposed(CancellationToken);

        reset.Status.Should().Be(TheoryStatus.Proposed);
        reset.ABTestRunId.Should().BeNull();

        var repo = services.GetRequiredService<IRepository<IOptimizationTheory>>();
        var reloaded = await repo.GetAsync(theory.Id, CancellationToken);
        reloaded.Status.Should().Be(TheoryStatus.Proposed);
    }

    [TestMethod]
    public async Task Reject_FromFailed_TransitionsToInvalidatedWithoutMetrics()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);
        var validating = await theory.SetValidating(CancellationToken);
        var failed = await validating.SetFailed(Guid.NewGuid(), CancellationToken);

        var rejected = await failed.Reject(CancellationToken);

        rejected.Status.Should().Be(TheoryStatus.Invalidated);
        rejected.PValue.Should().BeNull();

        var repo = services.GetRequiredService<IRepository<IOptimizationTheory>>();
        var reloaded = await repo.GetAsync(theory.Id, CancellationToken);
        reloaded.Status.Should().Be(TheoryStatus.Invalidated);
    }

    [TestMethod]
    public async Task SetValidated_WithProjectedPassRateAboveOne_Throws()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);
        var validating = await theory.SetValidating(CancellationToken);

        await FluentActions
            .Invoking(() => validating.SetValidated(Guid.NewGuid(), 0.5, 1.7, 0.02, Guid.NewGuid(), CancellationToken))
            .Should().ThrowAsync<Exception>();
    }

    [TestMethod]
    public async Task SetValidated_WithNegativePValue_Throws()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);
        var validating = await theory.SetValidating(CancellationToken);

        await FluentActions
            .Invoking(() => validating.SetValidated(Guid.NewGuid(), 0.5, 0.7, -3, Guid.NewGuid(), CancellationToken))
            .Should().ThrowAsync<Exception>();
    }

    [TestMethod]
    public async Task SetValidated_WithNaNBaselinePassRate_Throws()
    {
        IServiceProvider services = GetServices();
        var theory = await CreateTheory(services);
        var validating = await theory.SetValidating(CancellationToken);

        await FluentActions
            .Invoking(() => validating.SetValidated(Guid.NewGuid(), double.NaN, 0.7, 0.02, Guid.NewGuid(), CancellationToken))
            .Should().ThrowAsync<Exception>();
    }

    private async Task<ISystemPromptTheory> CreateTheory(IServiceProvider services)
    {
        var generator = services.GetRequiredService<IDomainEntityGenerator<ISystemPromptTheory>>();
        return await generator.CreateAsync(CancellationToken);
    }
}
