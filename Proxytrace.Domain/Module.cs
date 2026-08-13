using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Autofac;
using Nordstein.Core.Common.DependencyInjection;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.AgentVersion.Internal;
using Proxytrace.Domain.Inference;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Evaluator.Internal;
using Proxytrace.Domain.Events;
using Proxytrace.Domain.Events.Internal;
using Proxytrace.Domain.Message.Internal;
using Proxytrace.Domain.OptimizationProposal.Internal;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.Prompt.Internal;
using Proxytrace.Domain.Tools.Internal;

namespace Proxytrace.Domain;

public sealed class Module : Autofac.Module
{
    /// <summary>
    /// Adds the services for the AI.Agents.Cloud
    /// </summary>
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder.RegisterModule<Nordstein.Core.Common.Module>();

        // discover top-level domain entity/object interfaces — those that directly extend
        // IDomainEntity or IDomainObject, with no intermediate domain interface in between.
        var directBases = new HashSet<Type> { typeof(IDomainEntity), typeof(IDomainObject) };
        var domainInterfaceTypes = typeof(Module).Assembly
            .GetTypes()
            .Where(t => t is { IsInterface: true } && t != typeof(IDomainEntity) && t != typeof(IDomainObject)
                && !(t.IsGenericTypeDefinition && t.GetGenericTypeDefinition() == typeof(IDomainEntity<>)))
            .Where(t =>
            {
                // compute the "direct" interfaces (not reachable through another intermediate interface)
                var all = t.GetInterfaces();
                var transitive = all.SelectMany(i => i.GetInterfaces()).ToHashSet();
                var direct = all.Where(i => !transitive.Contains(i));
                return direct.Any(i => directBases.Contains(i)
                    || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainEntity<>)));
            })
            .ToList();

        // Dedupe (interface, implementation) pairs before registering: an implementation is often
        // reachable through several discovered interfaces (e.g. Agent via both IAgent and
        // IArchivable), and both passes resolve to the same most-derived interface. Note the
        // IArchivable pass is load-bearing — it is the only route that discovers entities whose
        // interface reaches IDomainEntity solely through IArchivable (IModelEndpoint, IModelProvider).
        var entityRegistrations = new HashSet<(Type InterfaceType, Type ImplementationType)>();
        foreach (Type domainInterfaceType in domainInterfaceTypes)
        {
            CollectEntityRegistrations(domainInterfaceType, entityRegistrations);
        }

        foreach ((Type interfaceType, Type implementationType) in entityRegistrations)
        {
            ConfigureEntity(builder, interfaceType, implementationType);
        }


        // Register generators for concrete domain object types (value objects without a repository)
        builder.RegisterAssemblyTypes(ThisAssembly)
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainObjectGenerator<>)))
            .Where(t => !t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainEntityGenerator<>)))
            .AsImplementedInterfaces();

        builder.RegisterType<ContentJsonConverter>()
            .As<JsonConverter>()
            .SingleInstance();

        builder.RegisterType<ToolArgumentsJsonConverter>()
            .As<JsonConverter>()
            .SingleInstance();

        builder.RegisterType<EvaluatorGenerator>()
            .AsImplementedInterfaces();

        builder.RegisterType<OptimizationProposalGenerator>()
            .AsImplementedInterfaces();

        builder.RegisterType<OptimizationTheory.Internal.OptimizationTheoryGenerator>()
            .AsImplementedInterfaces();

        builder.RegisterType<EntityEventService>()
            .As<IEntityEventService>()
            .SingleInstance();

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

    private static void CollectEntityRegistrations(
        Type domainInterfaceType,
        ISet<(Type InterfaceType, Type ImplementationType)> registrations)
    {
        // find implementation of domainInterfaceType
        foreach (var domainObjectType in domainInterfaceType.GetImplementations())
        {
            // find closest domainInterfaceType
            // e.g. IEvaluator -> IAgenticEvaluator -> IPolitenessEvaluator should pick IPolitenessEvaluator
            var correctDomainInterfaceType = domainObjectType.GetInterfaces()
                .Where(domainInterfaceType.IsAssignableFrom)
                .OrderByDescending(i => i.GetInterfaces().Length) // pick the most derived interface
                .First();

            registrations.Add((correctDomainInterfaceType, domainObjectType));
        }
    }

    private static void ConfigureEntity(ContainerBuilder builder, Type domainInterfaceType, Type domainObjectType)
    {
        builder.RegisterType(domainObjectType)
            .As(domainInterfaceType)
            .OnActivated(context =>
            {
                if (context.Instance is IValidatableObject validatable)
                {
                    Validator.ValidateObject(validatable, new ValidationContext(context.Instance), true);
                }
            });

        // register generator
        var generatorInterfaceType = typeof(IDomainObjectGenerator<>).MakeGenericType(domainInterfaceType);
        var generatorImplementationType = typeof(Module).Assembly
            .GetTypes()
            .FirstOrDefault(t => generatorInterfaceType.IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false });
        if (generatorImplementationType is null)
        {
            throw new InvalidOperationException($"No implementation of {generatorInterfaceType.FullName} found");
        }
        // register the generator implementation as all interfaces it implements
        var generatorInterfaces = generatorImplementationType.GetInterfaces();
        foreach (var generatorInterface in generatorInterfaces)
        {
            builder.RegisterType(generatorImplementationType).As(generatorInterface);
        }
    }
}
