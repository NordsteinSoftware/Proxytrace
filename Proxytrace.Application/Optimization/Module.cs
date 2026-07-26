using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Application.Optimization.Internal;
using Proxytrace.Application.Optimization.Internal.Adoption;
using Proxytrace.Application.Optimization.Internal.Evidence;
using Proxytrace.Application.Optimization.Internal.Validation;
using Proxytrace.Common.DependencyInjection;

namespace Proxytrace.Application.Optimization;

internal class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        // Fallback gating for hosts that bind no configuration (tests, tooling). The API registers
        // the configured instance before the modules load, so PreserveExistingDefaults leaves that
        // one in charge wherever it exists and this one only fills the gap.
        builder.RegisterInstance(new OptimizationOptions())
            .PreserveExistingDefaults();

        builder.RegisterType<CompositeOptimizer>()
            .As<IOptimizer>()
            .SingleInstance();

        builder.RegisterType<SwitchModelOptimizer>()
            .As<IOptimizerImplementation>();

        builder.RegisterType<UpdateSystemPromptOptimizer>()
            .As<IOptimizerImplementation>();

        builder.RegisterType<UpdateToolDefinitionOptimizer>()
            .As<IOptimizerImplementation>();

        builder.RegisterType<SystemPromptTheoryValidator>()
            .As<ITheoryValidator>();

        builder.RegisterType<ToolUpdateTheoryValidator>()
            .As<ITheoryValidator>();

        builder.RegisterType<ModelSwitchTheoryValidator>()
            .As<ITheoryValidator>();

        builder.RegisterType<OptimizerEvidenceBuilder>()
            .As<IOptimizerEvidenceBuilder>();

        builder.RegisterType<TheoryValidationService>()
            .As<ITheoryValidationService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterServiceCollection(services =>
            services.AddHostedService(sc => sc.GetRequiredService<TheoryValidationService>()));

        builder.RegisterType<OptimizerService>()
            .As<IOptimizerService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterServiceCollection(services =>
            services.AddHostedService(sc => sc.GetRequiredService<OptimizerService>()));

        builder.RegisterType<ProposalAdoptionMatcher>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ProposalAdoptionService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterServiceCollection(services =>
            services.AddHostedService(sc => sc.GetRequiredService<ProposalAdoptionService>()));
    }
}
