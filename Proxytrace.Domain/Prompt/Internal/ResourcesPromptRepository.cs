using Nordstein.Core.AI.Prompts;
using System.Resources;
using Nordstein.Core.Common.Validation;

namespace Proxytrace.Domain.Prompt.Internal;

/// <summary>
/// A repository that loads prompt templates from embedded resources.
/// </summary>
internal class ResourcesPromptRepository : IPromptTemplateRepository
{
    private readonly IReadOnlyCollection<ResourceManager> resources;
    private readonly IPromptTemplate.Create createTemplate;

    public ResourcesPromptRepository(
        IReadOnlyCollection<ResourceManager> resources,
        IPromptTemplate.Create createTemplate)
    {
        this.resources = resources;
        this.createTemplate = createTemplate;
    }

    /// <inheritdoc />
    public async Task<IPromptTemplate> GetAsync(string name, CancellationToken cancellationToken = default)
        => await FindAsync(name, cancellationToken) ?? throw new PromptNotFoundException(name);

    /// <inheritdoc />
    public Task<IPromptTemplate?> FindAsync(string name, CancellationToken cancellationToken = default)
    {
        string? templateContent = resources.Select(res =>
            {
                try
                {
                    return res.GetString(name);
                }
                catch (MissingManifestResourceException)
                {
                    // Resource not found in this assembly, ignore and continue
                    return null;
                }
            })
            .FirstOrDefault(content => !string.IsNullOrWhiteSpace(content));
        if (string.IsNullOrWhiteSpace(templateContent))
        {
            return Task.FromResult<IPromptTemplate?>(null);
        }

        IPromptTemplate template = createTemplate(name, templateContent);
        template.Validate();
        return Task.FromResult<IPromptTemplate?>(template);
    }
}
