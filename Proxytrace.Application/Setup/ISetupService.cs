using Proxytrace.Domain.ModelProvider;

namespace Proxytrace.Application.Setup;

/// <summary>
/// Drives the first-run setup wizard: validates a provider connection, creates the initial provider/endpoint/project/admin-user, and gates further use until setup completes.
/// </summary>
public interface ISetupService
{
    /// <summary>
    /// Runs the full first-run setup: creates the provider, endpoint, project, and default agent, then provisions the first admin user.
    /// </summary>
    Task<SetupResult> CompleteAsync(SetupInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates provider credentials by listing models without persisting any state.
    /// </summary>
    Task<ProviderConnectionResult> TestProviderConnectionAsync(ProviderConnectionInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the model names available at the given provider endpoint, used to populate the setup wizard's model picker.
    /// </summary>
    Task<IReadOnlyList<string>> ListProviderModelsAsync(ProviderConnectionInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the initial admin account and returns a session token; callable only before any users exist.
    /// </summary>
    Task<FirstAdminResult> CreateFirstAdminAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when at least one user account exists, used to determine whether first-run setup has already completed.
    /// </summary>
    Task<bool> AnyUsersExistAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Parameters for testing or creating a model provider connection during setup.
/// </summary>
public record ProviderConnectionInput(
    string ProviderName,
    Uri ProviderEndpoint,
    string ProviderUpstreamApiKey,
    ModelProviderKind ProviderKind);

/// <summary>
/// Full first-run setup payload: provider details, an initial model, and the first project name.
/// </summary>
public record SetupInput(
    string ProviderName,
    Uri ProviderEndpoint,
    string ProviderUpstreamApiKey,
    ModelProviderKind ProviderKind,
    string ModelName,
    string ProjectName);

/// <summary>
/// Identifiers for the provider, endpoint, and project created by the setup wizard.
/// </summary>
public record SetupResult(
    Guid ProviderId,
    Guid EndpointId,
    Guid ProjectId);

/// <summary>
/// Session credentials for the first admin account created during setup.
/// </summary>
public record FirstAdminResult(Guid UserId, string Token, DateTimeOffset ExpiresAt);
