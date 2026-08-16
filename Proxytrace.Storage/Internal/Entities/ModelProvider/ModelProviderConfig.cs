using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using Nordstein.Core.Common.Security;
using Proxytrace.Domain.Security;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain.ModelProvider;

namespace Proxytrace.Storage.Internal.Entities.ModelProvider;

internal class ModelProviderConfig : AbstractEntityConfiguration<ModelProviderEntity>, IMapper<IModelProvider, ModelProviderEntity>
{
    private readonly IModelProvider.CreateExisting factory;

    // Lazy so the mapper can be constructed (and the EF model built) without a Data Protection key
    // ring — the design-time migration tooling builds the model via Configure() and never calls
    // Map(), so it must not force IDataProtectionProvider resolution.
    private readonly Lazy<ISecretProtector> protector;

    // ISecretIndexer, not ISecretHasher: the upstream key is operator-entered, so it may be a
    // dictionary word (self-hosted backends conventionally use "EMPTY"/"ollama"/"sk-1234"). An
    // unkeyed index over that falls to a wordlist and undoes the encryption on the column beside it.
    private readonly ISecretIndexer indexer;
    private readonly ILogger<ModelProviderConfig> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelProviderConfig"/> class.
    /// </summary>
    public ModelProviderConfig(
        IModelProvider.CreateExisting factory,
        Lazy<ISecretProtector> protector,
        ISecretIndexer indexer,
        ILogger<ModelProviderConfig> logger)
    {
        this.factory = factory;
        this.protector = protector;
        this.indexer = indexer;
        this.logger = logger;
    }

    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<ModelProviderEntity> builder)
    {
        builder.HasIndex(e => e.Name).IsUnique();
        // Blind index for the proxy's upstream-key auth path (FindByApiKeyAsync); the ApiKey column
        // now holds non-deterministic ciphertext and cannot be looked up directly.
        builder.HasIndex(e => e.ApiKeyLookupHash);
        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
        builder.Property(e => e.Endpoint).HasMaxLength(2048).IsRequired();
        // Ciphertext is far larger than the plaintext key — drop the 512 cap.
        builder.Property(e => e.ApiKey).IsRequired();
        // Wide enough for the scheme-prefixed keyed index ("hmac1:" + 64 hex chars) as well as the
        // bare 64-char legacy hash that predates it.
        builder.Property(e => e.ApiKeyLookupHash).HasMaxLength(128);
        builder.Property(e => e.Kind).IsRequired();
        builder.HasIndex(e => e.IsArchived);
    }

    /// <summary>
    /// Maps.
    /// </summary>
    public Task<IModelProvider> Map(ModelProviderEntity stored, CancellationToken cancellationToken = default)
        => factory(stored.Name, new Uri(stored.Endpoint), Decrypt(stored.ApiKey), stored.Kind, stored).ToTaskResult();

    /// <summary>
    /// Maps.
    /// </summary>
    public Task<ModelProviderEntity> Map(IModelProvider domain, CancellationToken cancellationToken = default)
        => new ModelProviderEntity
        {
            Id = domain.Id,
            Name = domain.Name,
            Endpoint = domain.Endpoint.ToString(),
            ApiKey = protector.Value.Protect(domain.ApiKey),
            ApiKeyLookupHash = indexer.Index(domain.ApiKey),
            Kind = domain.Kind,
            IsArchived = domain.IsArchived,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();

    /// <summary>
    /// Decrypts the stored upstream key, degrading to an empty string (rather than throwing) when the
    /// ciphertext can't be decrypted — e.g. an ephemeral Data Protection key ring after a restart
    /// without <c>PROXYTRACE_DATA_DIR</c>. Listing and the proxy resolver must never crash; the
    /// provider simply fails to authenticate upstream until an operator re-enters the key. Mirrors
    /// <c>EmailSettingsStore.DecryptPassword</c>.
    /// </summary>
    private string Decrypt(string cipher)
    {
        try
        {
            return protector.Value.Unprotect(cipher);
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning(ex, "Could not decrypt a stored provider API key; treating it as unset.");
            return string.Empty;
        }
    }
}
