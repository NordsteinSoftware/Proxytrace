using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Application.Evaluator;
using Proxytrace.Application.Evaluator.Internal;
using Proxytrace.Domain;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.Project;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests;

[TestClass]
public sealed class DefaultEvaluatorProvisionerTests : BaseTest<Module>
{
    private IServiceProvider GetProvisionerServices() =>
        GetServices(builder =>
        {
            builder.RegisterType<AgenticEvaluatorPresets>().As<IAgenticEvaluatorPresets>();
            builder.RegisterType<DefaultEvaluatorProvisioner>().As<IDefaultEvaluatorProvisioner>();
        });

    [TestMethod]
    public async Task EnsureDefaultEvaluators_OnFreshProject_CreatesExactMatchAndAllPresets()
    {
        var services = GetProvisionerServices();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>()
            .GetOrCreateAsync(CancellationToken);
        var presets = services.GetRequiredService<IAgenticEvaluatorPresets>();
        var provisioner = services.GetRequiredService<IDefaultEvaluatorProvisioner>();

        await provisioner.EnsureDefaultEvaluatorsAsync(project, CancellationToken);

        var evaluators = await services.GetRequiredService<IEvaluatorRepository>()
            .GetByProjectAsync(project.Id, CancellationToken);

        evaluators.Should().Contain(e => e.Kind == EvaluatorKind.ExactMatch);
        foreach (var preset in presets.GetAll())
        {
            evaluators.Should().Contain(e => e.Kind == EvaluatorKind.Agentic && e.Name == preset.Name);
        }
        evaluators.Should().HaveCount(1 + presets.GetAll().Count);
    }

    [TestMethod]
    public async Task EnsureDefaultEvaluators_CalledTwice_IsIdempotent()
    {
        var services = GetProvisionerServices();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>()
            .GetOrCreateAsync(CancellationToken);
        var presets = services.GetRequiredService<IAgenticEvaluatorPresets>();
        var provisioner = services.GetRequiredService<IDefaultEvaluatorProvisioner>();

        await provisioner.EnsureDefaultEvaluatorsAsync(project, CancellationToken);
        await provisioner.EnsureDefaultEvaluatorsAsync(project, CancellationToken);

        var evaluators = await services.GetRequiredService<IEvaluatorRepository>()
            .GetByProjectAsync(project.Id, CancellationToken);

        evaluators.Should().HaveCount(1 + presets.GetAll().Count);
    }

    [TestMethod]
    public async Task EnsureDefaultEvaluators_WithExistingDefaults_DoesNotDuplicate()
    {
        var services = GetProvisionerServices();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>()
            .GetOrCreateAsync(CancellationToken);
        var evaluatorRepo = services.GetRequiredService<IEvaluatorRepository>();
        var provisioner = services.GetRequiredService<IDefaultEvaluatorProvisioner>();

        await provisioner.EnsureDefaultEvaluatorsAsync(project, CancellationToken);
        var before = await evaluatorRepo.GetByProjectAsync(project.Id, CancellationToken);

        // A second pass (the startup backfill scenario) must find every default already present.
        await provisioner.EnsureDefaultEvaluatorsAsync(project, CancellationToken);
        var after = await evaluatorRepo.GetByProjectAsync(project.Id, CancellationToken);

        after.Select(e => e.Name).Should().BeEquivalentTo(before.Select(e => e.Name));
    }
}
