using Autofac;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nordstein.Core.Common.Security;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.Security;

namespace Proxytrace.Storage;

/// <summary>
/// Design-time factory for StorageDbContext, used by EF Core tooling (dotnet ef migrations add, etc.).
/// </summary>
[UsedImplicitly]
internal class StorageDbContextFactory : IDesignTimeDbContextFactory<StorageDbContext>
{
    public StorageDbContext CreateDbContext(string[] args)
    {
        // Environment variables are added LAST so they win over the JSON files — this is what makes
        // `ConnectionStrings__Default=...` (documented in docs/database.md) actually target the
        // intended database. Without it the env var is silently ignored and the tooling runs against
        // whatever appsettings.json points at (typically the local dev DB), which can clobber it.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set ConnectionStrings:Default via the ConnectionStrings__Default environment variable "
                + "or in appsettings.json / appsettings.development.json.");

        // Migrations are PostgreSQL-only. Supply a PostgreSQL connection string at design time,
        // e.g. via the ConnectionStrings__Default environment variable.
        var storageConfig = StorageConfiguration.Postgres(connectionString);

        var containerBuilder = new ContainerBuilder();
        containerBuilder.RegisterModule(new Module(_ => storageConfig));

        // Since #270, Storage references only Domain and no longer registers the services the storage
        // model-building graph expects from the Application/Infrastructure layers (the at-rest secret
        // seam, the agent-name generator, the provider client). EF model-building still constructs every
        // entity configuration — and the repositories some of them inject — so those seams must be
        // *registered*, even though none is invoked while the model is built (their value converters and
        // operations are captured lazily / never called). Register local stubs so `dotnet ef` tooling can
        // build the context without loading Application or the Data Protection key ring. This mirrors the
        // lean proxy host, which resolves the same storage graph with the same stubs (see
        // Proxytrace.Proxy.Module / #270).
        var designTimeSecrets = new DesignTimeSecretSeam();
        containerBuilder.RegisterInstance(designTimeSecrets).As<ISecretProtector>().As<ISecretHasher>().As<ISecretIndexer>();
        containerBuilder.RegisterType<DesignTimeAgentNameGenerator>().As<IAgentNameGenerator>().SingleInstance();
        containerBuilder.RegisterType<DesignTimeProviderClient>().As<IProviderClient>();

        containerBuilder.RegisterInstance(NullLoggerFactory.Instance).As<ILoggerFactory>();
        containerBuilder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>));
        var container = containerBuilder.Build();

        return container.Resolve<StorageDbContext>();
    }

    /// <summary>
    /// Design-time-only no-op secret seam. EF model-building constructs the secret-bearing entity
    /// configurations but never runs their value converters, so the real Data Protection-backed
    /// implementations (Proxytrace.Infrastructure.Security) are not needed by <c>dotnet ef</c> tooling.
    /// </summary>
    private sealed class DesignTimeSecretSeam : ISecretProtector, ISecretHasher, ISecretIndexer
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string ciphertext) => ciphertext;

        public string Hash(string value) => value;

        public string Index(string value) => value;

        public string LegacyIndex(string value) => value;

        public bool IsKeyed => false;
    }

    /// <summary>
    /// Design-time stub for <see cref="IAgentNameGenerator"/> (implemented in the Application layer,
    /// which design-time tooling does not load). The storage model-building graph injects it into
    /// <c>AgentRepository</c>; it is never invoked while EF builds the model.
    /// </summary>
    private sealed class DesignTimeAgentNameGenerator : IAgentNameGenerator
    {
        public Task<string> GenerateNameAsync(
            IPromptTemplate systemPrompt,
            IProject project,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Agent name generation is not available at design time.");
    }

    /// <summary>
    /// Design-time stub for <see cref="IProviderClient"/> (implemented in the Application layer).
    /// Reconstituting a <c>ModelProvider</c> in the storage graph needs an
    /// <see cref="IProviderClient.Factory"/>; it is never invoked while EF builds the model.
    /// </summary>
    private sealed class DesignTimeProviderClient : IProviderClient
    {
        public Task<ProviderConnectionResult> VerifyConnectionAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Provider client operations are not available at design time.");

        public Task<IReadOnlyList<PricedModel>> GetModelsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Provider client operations are not available at design time.");
    }
}
