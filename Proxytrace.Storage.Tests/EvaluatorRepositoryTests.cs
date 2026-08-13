using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestSuite;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// Exhaustive round-trip test for every <see cref="EvaluatorKind"/>.
/// Adding a new kind without implementing storage will cause <see cref="AllKinds_CanBePersisted"/> to fail.
/// </summary>
[TestClass]
public sealed class EvaluatorPersistenceTests : BaseTest<Module>
{
    public static IEnumerable<object[]> AllEvaluatorKinds
        => Enum.GetValues<EvaluatorKind>().Select(k => new object[] { k });

    [TestMethod]
    [DynamicData(nameof(AllEvaluatorKinds))]
    public async Task AllKinds_CanBePersisted(EvaluatorKind kind)
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<IEvaluator>>();

        var evaluator = await CreateForKind(kind, services);
        var retrieved = await repository.GetAsync(evaluator.Id, CancellationToken);

        retrieved.Id.Should().Be(evaluator.Id);
        retrieved.Kind.Should().Be(kind);
    }

    private async Task<IEvaluator> CreateForKind(EvaluatorKind kind, IServiceProvider services)
    {
        IEvaluatorGenerator generator = services.GetRequiredService<IEvaluatorGenerator>();
        return await generator.CreateAsync(kind, CancellationToken);
    }
}

[TestClass]
public sealed class EvaluatorRepositoryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task AddAsync_ExactMatchEvaluator_Persists()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<IEvaluator>>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>();

        var evaluator = await generator.CreateAsync(CancellationToken);

        var retrieved = await repository.GetAsync(evaluator.Id, CancellationToken);

        retrieved.Id.Should().Be(evaluator.Id);
        retrieved.Kind.Should().Be(EvaluatorKind.ExactMatch);
        retrieved.Should().BeAssignableTo<IExactMatchEvaluator>();
    }

    [TestMethod]
    public async Task AddAsync_AgenticEvaluator_PersistsWithSystemMessage()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<IEvaluator>>();
        var factory = services.GetRequiredService<IAgenticEvaluator.CreateNew>();
        var agentGenerator = services.GetRequiredService<IAgentGenerator>();

        var agent = await agentGenerator.CreateAsync("Test Evaluator", "Judge whether the response is correct.", isSystemAgent: true);
        var evaluator = factory(agent);
        var added = await repository.AddAsync(evaluator, CancellationToken);

        var retrieved = await repository.GetAsync(added.Id, CancellationToken);

        retrieved.Id.Should().Be(added.Id);
        retrieved.Kind.Should().Be(EvaluatorKind.Agentic);
        IAgenticEvaluator custom = retrieved.Should().BeAssignableTo<IAgenticEvaluator>().Subject;
        custom.Agent.SystemPrompt.Template.Should().Be("Judge whether the response is correct.");
    }

    [TestMethod]
    public async Task GetByProjectAsync_FiltersByProject()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IEvaluatorRepository>();
        var exactFactory = services.GetRequiredService<IExactMatchEvaluator.CreateNew>();
        var projectGenerator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();

        var projectA = await projectGenerator.CreateAsync(CancellationToken);
        var projectB = await projectGenerator.CreateAsync(CancellationToken);
        var inA = await repository.AddAsync(exactFactory(projectA), CancellationToken);
        var inB = await repository.AddAsync(exactFactory(projectB), CancellationToken);

        var resultsA = await repository.GetByProjectAsync(projectA.Id, CancellationToken);

        resultsA.Should().Contain(e => e.Id == inA.Id);
        resultsA.Should().NotContain(e => e.Id == inB.Id);
    }

    [TestMethod]
    public async Task GetAllAsync_ReturnsAllEvaluators()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<IEvaluator>>();
        var exactFactory = services.GetRequiredService<IExactMatchEvaluator.CreateNew>();
        var agenticFactory = services.GetRequiredService<IAgenticEvaluator.CreateNew>();
        var projectGenerator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();

        var project = await projectGenerator.CreateAsync(CancellationToken);
        var exact = exactFactory(project);
        var agentGenerator = services.GetRequiredService<IAgentGenerator>();
        var agent = await agentGenerator.CreateAsync("Test Evaluator", "Evaluate.", isSystemAgent: true);
        var agentic = agenticFactory(agent);
        await repository.AddAsync(exact, CancellationToken);
        await repository.AddAsync(agentic, CancellationToken);

        var all = await repository.GetAllAsync(CancellationToken);

        all.Should().HaveCountGreaterThanOrEqualTo(2);
        all.Should().Contain(e => e.Id == exact.Id && e.Kind == EvaluatorKind.ExactMatch);
        all.Should().Contain(e => e.Id == agentic.Id && e.Kind == EvaluatorKind.Agentic);
    }
}

[TestClass]
public sealed class EvaluatorArchiveTests : BaseTest<Module>
{
    [TestMethod]
    public async Task ArchiveAsync_ExcludesEvaluatorFromGetByProject()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IEvaluatorRepository>();
        var exactFactory = services.GetRequiredService<IExactMatchEvaluator.CreateNew>();
        var projectGenerator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();

        var project = await projectGenerator.CreateAsync(CancellationToken);
        var evaluator = await repository.AddAsync(exactFactory(project), CancellationToken);

        await repository.ArchiveAsync(evaluator.Id, CancellationToken);

        var results = await repository.GetByProjectAsync(project.Id, CancellationToken);
        results.Should().NotContain(e => e.Id == evaluator.Id);
    }

    [TestMethod]
    public async Task ArchiveAsync_ExcludesEvaluatorFromGetAll()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IEvaluatorRepository>();
        var exactFactory = services.GetRequiredService<IExactMatchEvaluator.CreateNew>();
        var projectGenerator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();

        var project = await projectGenerator.CreateAsync(CancellationToken);
        var evaluator = await repository.AddAsync(exactFactory(project), CancellationToken);

        await repository.ArchiveAsync(evaluator.Id, CancellationToken);

        var all = await repository.GetAllAsync(CancellationToken);
        all.Should().NotContain(e => e.Id == evaluator.Id);
    }

    [TestMethod]
    public async Task ArchiveAsync_KeepsEvaluatorResolvableById()
    {
        // The whole point of archiving: historical test results live-fetch the evaluator by id at
        // map time, so a by-id lookup must still resolve it (flagged as archived).
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IEvaluatorRepository>();
        var exactFactory = services.GetRequiredService<IExactMatchEvaluator.CreateNew>();
        var projectGenerator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();

        var project = await projectGenerator.CreateAsync(CancellationToken);
        var evaluator = await repository.AddAsync(exactFactory(project), CancellationToken);

        await repository.ArchiveAsync(evaluator.Id, CancellationToken);

        var retrieved = await repository.GetAsync(evaluator.Id, CancellationToken);
        retrieved.Id.Should().Be(evaluator.Id);
        retrieved.IsArchived.Should().BeTrue();
    }

    [TestMethod]
    public async Task ArchiveAsync_DetachesEvaluatorFromSuites()
    {
        IServiceProvider services = GetServices();
        var evaluatorRepository = services.GetRequiredService<IEvaluatorRepository>();
        var suiteRepository = services.GetRequiredService<IRepository<ITestSuite>>();
        var testCaseGenerator = services.GetRequiredService<IDomainEntityGenerator<ITestCase>>();
        var projectGenerator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var exactFactory = services.GetRequiredService<IExactMatchEvaluator.CreateNew>();
        var agentGenerator = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();
        var suiteFactory = services.GetRequiredService<ITestSuite.CreateNew>();

        var project = await projectGenerator.CreateAsync(CancellationToken);
        var evaluator = await evaluatorRepository.AddAsync(exactFactory(project), CancellationToken);
        var agent = await agentGenerator.CreateAsync(CancellationToken);
        var testCase = await testCaseGenerator.CreateAsync(CancellationToken);
        var suite = await suiteRepository.AddAsync(
            suiteFactory("Archive detach suite", agent, [evaluator], [testCase]), CancellationToken);

        await evaluatorRepository.ArchiveAsync(evaluator.Id, CancellationToken);

        var reloadedSuite = await suiteRepository.GetAsync(suite.Id, CancellationToken);
        reloadedSuite.Evaluators.Should().NotContain(e => e.Id == evaluator.Id);
        // The evaluator row itself survives so history still resolves.
        var retrieved = await evaluatorRepository.GetAsync(evaluator.Id, CancellationToken);
        retrieved.IsArchived.Should().BeTrue();
    }

    [TestMethod]
    public async Task ArchiveAsync_MissingEvaluator_ReturnsFalse()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IEvaluatorRepository>();

        var archived = await repository.ArchiveAsync(Guid.NewGuid(), CancellationToken);

        archived.Should().BeFalse();
    }

    [TestMethod]
    public async Task ArchiveAsync_AlreadyArchived_ReturnsFalseButStaysArchived()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IEvaluatorRepository>();
        var exactFactory = services.GetRequiredService<IExactMatchEvaluator.CreateNew>();
        var projectGenerator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();

        var project = await projectGenerator.CreateAsync(CancellationToken);
        var evaluator = await repository.AddAsync(exactFactory(project), CancellationToken);

        var first = await repository.ArchiveAsync(evaluator.Id, CancellationToken);
        var second = await repository.ArchiveAsync(evaluator.Id, CancellationToken);

        // Only the first call transitions the row; a repeated archive is a no-op (false) so callers
        // can 404 and skip auditing. The row itself stays archived and resolvable by id.
        first.Should().BeTrue();
        second.Should().BeFalse();
        (await repository.GetAsync(evaluator.Id, CancellationToken)).IsArchived.Should().BeTrue();
    }
}

[TestClass]
public sealed class TestSuiteEvaluatorRelationshipTests : BaseTest<Module>
{
    [TestMethod]
    public async Task AddAsync_WithMultipleEvaluators_PersistsAll()
    {
        IServiceProvider services = GetServices();
        var suiteRepository = services.GetRequiredService<IRepository<ITestSuite>>();
        var evaluatorRepository = services.GetRequiredService<IRepository<IEvaluator>>();
        var testCaseGenerator = services.GetRequiredService<IDomainEntityGenerator<ITestCase>>();
        var projectGenerator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var exactFactory = services.GetRequiredService<IExactMatchEvaluator.CreateNew>();
        var agenticFactory = services.GetRequiredService<IAgenticEvaluator.CreateNew>();
        var suiteFactory = services.GetRequiredService<ITestSuite.CreateNew>();

        var testCase = await testCaseGenerator.CreateAsync(CancellationToken);
        var project = await projectGenerator.CreateAsync(CancellationToken);
        var exact = await evaluatorRepository.AddAsync(exactFactory(project), CancellationToken);
        var agentGenerator = services.GetRequiredService<IAgentGenerator>();
        var agent = await agentGenerator.CreateAsync("Test Evaluator", "Evaluate.", isSystemAgent: true);
        var agentic = await agenticFactory(agent).AddAsync(CancellationToken);

        var suite = suiteFactory("Multi-eval suite", agent, [exact, agentic], [testCase]);
        var added = await suiteRepository.AddAsync(suite, CancellationToken);

        added.Evaluators.Should().HaveCount(2);
        added.Evaluators.Should().Contain(e => e.Id == exact.Id);
        added.Evaluators.Should().Contain(e => e.Id == agentic.Id);
    }

    [TestMethod]
    public async Task GetAsync_WithMultipleEvaluators_LoadsAll()
    {
        IServiceProvider services = GetServices();
        var suiteRepository = services.GetRequiredService<IRepository<ITestSuite>>();
        var evaluatorRepository = services.GetRequiredService<IRepository<IEvaluator>>();
        var testCaseGenerator = services.GetRequiredService<IDomainEntityGenerator<ITestCase>>();
        var projectGenerator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var exactFactory = services.GetRequiredService<IExactMatchEvaluator.CreateNew>();
        var agenticFactory = services.GetRequiredService<IAgenticEvaluator.CreateNew>();
        var suiteFactory = services.GetRequiredService<ITestSuite.CreateNew>();

        var testCase = await testCaseGenerator.CreateAsync(CancellationToken);
        var project = await projectGenerator.CreateAsync(CancellationToken);
        var exact = await evaluatorRepository.AddAsync(exactFactory(project), CancellationToken);
        var agentGenerator = services.GetRequiredService<IAgentGenerator>();
        var agent = await agentGenerator.CreateAsync("Test Evaluator", "Evaluate.", isSystemAgent: true);
        var agentic = await agenticFactory(agent).AddAsync();

        var suite = suiteFactory("Get suite", agent, [exact, agentic], [testCase]);
        await suiteRepository.AddAsync(suite, CancellationToken);

        var retrieved = await suiteRepository.GetAsync(suite.Id, CancellationToken);

        retrieved.Id.Should().Be(suite.Id);
        retrieved.Evaluators.Should().HaveCount(2);
        retrieved.Evaluators.Should().Contain(e => e.Id == exact.Id && e.Kind == EvaluatorKind.ExactMatch);
        var agenticRetrieved = retrieved.Evaluators.First(e => e.Id == agentic.Id);
        agenticRetrieved.Kind.Should().Be(EvaluatorKind.Agentic);
        agenticRetrieved.Should().BeAssignableTo<IAgenticEvaluator>();
    }

    [TestMethod]
    public async Task UpdateAsync_AddingEvaluator_UpdatesRelationship()
    {
        IServiceProvider services = GetServices();
        var suiteRepository = services.GetRequiredService<IRepository<ITestSuite>>();
        var evaluatorRepository = services.GetRequiredService<IRepository<IEvaluator>>();
        var testCaseGenerator = services.GetRequiredService<IDomainEntityGenerator<ITestCase>>();
        var projectGenerator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var exactFactory = services.GetRequiredService<IExactMatchEvaluator.CreateNew>();
        var agenticFactory = services.GetRequiredService<IAgenticEvaluator.CreateNew>();
        var suiteFactory = services.GetRequiredService<ITestSuite.CreateNew>();
        var suiteExistingFactory = services.GetRequiredService<ITestSuite.CreateExisting>();

        var testCase = await testCaseGenerator.CreateAsync(CancellationToken);
        var project = await projectGenerator.CreateAsync(CancellationToken);
        var exact = await evaluatorRepository.AddAsync(exactFactory(project), CancellationToken);
        var agentGenerator = services.GetRequiredService<IAgentGenerator>();
        var agent = await agentGenerator.CreateAsync("Test Evaluator", "Evaluate.", isSystemAgent: true);
        var agentic = await agenticFactory(agent).AddAsync();

        var suite = suiteFactory("Update suite", agent, [exact], [testCase]);
        var added = await suiteRepository.AddAsync(suite, CancellationToken);

        var updated = suiteExistingFactory(added.Name, added.Agent, [exact, agentic], added.TestCases, added);
        var result = await suiteRepository.UpdateAsync(updated, CancellationToken);

        result.Evaluators.Should().HaveCount(2);
        result.Evaluators.Should().Contain(e => e.Id == exact.Id);
        result.Evaluators.Should().Contain(e => e.Id == agentic.Id);

        var retrieved = await suiteRepository.GetAsync(result.Id, CancellationToken);
        retrieved.Evaluators.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task UpdateAsync_RemovingEvaluator_UpdatesRelationship()
    {
        IServiceProvider services = GetServices();
        var suiteRepository = services.GetRequiredService<IRepository<ITestSuite>>();
        var evaluatorRepository = services.GetRequiredService<IRepository<IEvaluator>>();
        var testCaseGenerator = services.GetRequiredService<IDomainEntityGenerator<ITestCase>>();
        var projectGenerator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var exactFactory = services.GetRequiredService<IExactMatchEvaluator.CreateNew>();
        var agenticFactory = services.GetRequiredService<IAgenticEvaluator.CreateNew>();
        var suiteFactory = services.GetRequiredService<ITestSuite.CreateNew>();
        var suiteExistingFactory = services.GetRequiredService<ITestSuite.CreateExisting>();

        var testCase = await testCaseGenerator.CreateAsync(CancellationToken);
        var project = await projectGenerator.CreateAsync(CancellationToken);
        var exact = await evaluatorRepository.AddAsync(exactFactory(project), CancellationToken);
        var agentGenerator = services.GetRequiredService<IAgentGenerator>();
        var agent = await agentGenerator.CreateAsync("Test Evaluator", "Evaluate.", isSystemAgent: true);
        var agentic = await agenticFactory(agent).AddAsync();


        var suite = suiteFactory("Remove eval suite", agent, [exact, agentic], [testCase]);
        var added = await suiteRepository.AddAsync(suite, CancellationToken);

        var updated = suiteExistingFactory(added.Name, added.Agent, [exact], added.TestCases, added);
        var result = await suiteRepository.UpdateAsync(updated, CancellationToken);

        result.Evaluators.Should().HaveCount(1);
        result.Evaluators.Should().Contain(e => e.Id == exact.Id);
        result.Evaluators.Should().NotContain(e => e.Id == agentic.Id);

        var retrieved = await suiteRepository.GetAsync(result.Id, CancellationToken);
        retrieved.Evaluators.Should().HaveCount(1);
        retrieved.Evaluators.Should().Contain(e => e.Id == exact.Id);
    }

    [TestMethod]
    public async Task AddAsync_WithSingleEvaluator_PersistsCorrectly()
    {
        IServiceProvider services = GetServices();
        var suiteRepository = services.GetRequiredService<IRepository<ITestSuite>>();
        var evaluatorRepository = services.GetRequiredService<IRepository<IEvaluator>>();
        var agentGenerator = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();
        var testCaseGenerator = services.GetRequiredService<IDomainEntityGenerator<ITestCase>>();
        var projectGenerator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var exactFactory = services.GetRequiredService<IExactMatchEvaluator.CreateNew>();
        var suiteFactory = services.GetRequiredService<ITestSuite.CreateNew>();

        var agent = await agentGenerator.CreateAsync(CancellationToken);
        var testCase = await testCaseGenerator.CreateAsync(CancellationToken);
        var project = await projectGenerator.CreateAsync(CancellationToken);
        var exact = await evaluatorRepository.AddAsync(exactFactory(project), CancellationToken);

        var suite = suiteFactory("Single eval suite", agent, [exact], [testCase]);
        var added = await suiteRepository.AddAsync(suite, CancellationToken);

        added.Evaluators.Should().HaveCount(1);
        added.Evaluators.Should().Contain(e => e.Id == exact.Id);

        var retrieved = await suiteRepository.GetAsync(added.Id, CancellationToken);
        retrieved.Evaluators.Should().HaveCount(1);
        retrieved.Evaluators.Single().Id.Should().Be(exact.Id);
    }
}
