using Proxytrace.Domain.ModelProvider;

namespace Proxytrace.Application.Setup;

/// <summary>
/// Service that provides setup functionality.
/// </summary>
public interface ISetupService
{
    Task<SetupResult> CompleteAsync(SetupInput input, CancellationToken cancellationToken = default);

    Task<ProviderConnectionResult> TestProviderConnectionAsync(ProviderConnectionInput input, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListProviderModelsAsync(ProviderConnectionInput input, CancellationToken cancellationToken = default);

    Task<FirstAdminResult> CreateFirstAdminAsync(string email, string password, CancellationToken cancellationToken = default);

    Task<bool> AnyUsersExistAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a provider connection input.
/// </summary>
public record ProviderConnectionInput(
    string ProviderName,
    Uri ProviderEndpoint,
    string ProviderUpstreamApiKey,
    ModelProviderKind ProviderKind);

/// <summary>
/// Represents a setup input.
/// </summary>
public record SetupInput(
    string ProviderName,
    Uri ProviderEndpoint,
    string ProviderUpstreamApiKey,
    ModelProviderKind ProviderKind,
    string ModelName,
    string ProjectName);

/// <summary>
/// Encapsulates the result of a setup operation.
/// </summary>
public record SetupResult(
    Guid ProviderId,
    Guid EndpointId,
    Guid ProjectId);

/// <summary>
/// Encapsulates the result of a first admin operation.
/// </summary>
public record FirstAdminResult(Guid UserId, string Token, DateTimeOffset ExpiresAt);
