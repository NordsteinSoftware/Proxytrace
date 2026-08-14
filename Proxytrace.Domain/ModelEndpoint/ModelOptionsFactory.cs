using Nordstein.Core.AI.Clients;
using Proxytrace.Domain.Model;

namespace Proxytrace.Domain.ModelEndpoint;

/// <summary>
/// Builds <see cref="ModelOptions"/> from product model/agent bindings — the product-side
/// replacement for the statics that could not move to Core (they reference <see cref="IModel"/>).
/// </summary>
public static class ModelOptionsFactory
{
    /// <summary>Options for a bare model completion: the model's name, no tools.</summary>
    public static ModelOptions FromModel(IModel model)
        => new(ModelName: model.Name, Tools: []);

    /// <summary>Options for an agent completion: the model's name and the agent's tools.</summary>
    public static ModelOptions FromAgent(Agent.IAgent agent, IModel model)
        => new(ModelName: model.Name, Tools: agent.Tools);
}
