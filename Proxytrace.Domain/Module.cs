using System.Text.Json.Serialization;
using Autofac;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.AgentVersion.Internal;
using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Evaluator.Internal;
using Nordstein.Core.AI.Messages.Internal;
using Proxytrace.Domain.OptimizationProposal.Internal;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.Prompt.Internal;

namespace Proxytrace.Domain;

public sealed class Module : Autofac.Module
{
    private const string RegisteredKey = "Proxytrace.Domain.Module.Registered";

    /// <summary>
    /// Adds Proxytrace domain services.
    /// </summary>
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);
        if (builder.Properties.ContainsKey(RegisteredKey))
        {
            return;
        }

        builder.Properties[RegisteredKey] = true;

        builder.RegisterModule(new Nordstein.Core.Domain.Module(typeof(Module).Assembly));
        builder.RegisterModule<Nordstein.Core.AI.Module>();

        builder.RegisterType<EvaluatorGenerator>()
            .AsImplementedInterfaces();

        builder.RegisterType<OptimizationProposalGenerator>()
            .AsImplementedInterfaces();

        builder.RegisterType<OptimizationTheory.Internal.OptimizationTheoryGenerator>()
            .AsImplementedInterfaces();

        builder.RegisterType<ResourcesPromptRepository>()
            .As<IPromptTemplateRepository>();

        builder.RegisterType<AgentVersionFingerprinter>()
            .As<IAgentVersionFingerprinter>()
            .SingleInstance();

        // IAgent.CreateNew is implemented manually because Agent's shell constructor produces
        // an agent without an IAgentVersion, and the initial version is stitched in afterward.
        // This factory hides the two-phase construction from callers.
        builder.Register<IAgent.CreateNew>(c =>
        {
            var shellFactory = c.Resolve<Func<string, IModelEndpoint, IProject, IModelParameters, bool, IAgent>>();
            var createVersion = c.Resolve<IAgentVersion.CreateNew>();
            return (name, systemPrompt, tools, endpoint, project, modelParameters, isSystemAgent) =>
            {
                var shell = (Agent.Internal.Agent)shellFactory(name, endpoint, project, modelParameters, isSystemAgent);
                var v1 = createVersion(project.Id, shell.Id, 1, systemPrompt, tools);
                return shell.WithInitialVersion(v1);
            };
        });
    }

}
