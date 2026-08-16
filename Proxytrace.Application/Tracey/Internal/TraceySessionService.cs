using Proxytrace.Domain.Project;

namespace Proxytrace.Application.Tracey.Internal;

internal sealed class TraceySessionService : ITraceySessionService
{
    private readonly ITraceyAgentProvisioner provisioner;

    /// <summary>
    /// Initializes a new instance of the <see cref="TraceySessionService"/> class.
    /// </summary>
    public TraceySessionService(ITraceyAgentProvisioner provisioner)
    {
        this.provisioner = provisioner;
    }

    /// <summary>
    /// Creates the session asynchronously.
    /// </summary>
    public async Task<TraceySessionResult> CreateSessionAsync(IProject project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var traceyAgent = await provisioner.EnsureTraceyAgentAsync(project, cancellationToken);

        return new TraceySessionResult(
            Model: project.SystemEndpoint.Model.Name,
            AgentId: traceyAgent.Id);
    }
}
