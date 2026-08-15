using System.ComponentModel.DataAnnotations;
using System.Text;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.ModelProvider.Internal;

internal record ModelProvider : DomainEntity<IModelProvider>, IModelProvider
{
    private readonly IProviderClient.Factory clientFactory;
    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// Gets the endpoint.
    /// </summary>
    public Uri Endpoint { get; }
    /// <summary>
    /// Gets the api key.
    /// </summary>
    public string ApiKey { get; }
    /// <summary>
    /// Gets the kind.
    /// </summary>
    public ModelProviderKind Kind { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelProvider"/> class.
    /// </summary>
    public ModelProvider(
        string name,
        Uri endpoint,
        string apiKey,
        ModelProviderKind kind,
        IProviderClient.Factory clientFactory,
        IRepository<IModelProvider> repository) : base(repository)
    {
        this.clientFactory = clientFactory;
        Name = name;
        Endpoint = endpoint;
        ApiKey = apiKey;
        Kind = kind;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelProvider"/> class.
    /// </summary>
    public ModelProvider(
        string name, 
        Uri endpoint,
        string apiKey,
        ModelProviderKind kind,
        IDomainEntityData existing,
        IProviderClient.Factory clientFactory,
        IRepository<IModelProvider> repository) : base(existing, repository)
    {
        this.clientFactory = clientFactory;
        Name = name;
        Endpoint = endpoint;
        ApiKey = apiKey;
        Kind = kind;
    }
    
    /// <summary>
    /// Creates the client.
    /// </summary>
    public IProviderClient CreateClient()
        => clientFactory(this);

    // Redact the secret upstream credential from the record's generated ToString()/PrintMembers so
    // the key never leaks into a log line, exception message, or debugger string. ApiKey stays a
    // public member (the proxy reads it; equality keeps it) — only its textual rendering is masked.
    protected override bool PrintMembers(StringBuilder builder)
    {
        if (base.PrintMembers(builder))
        {
            builder.Append(", ");
        }

        builder.Append("Name = ").Append(Name)
            .Append(", Endpoint = ").Append(Endpoint)
            .Append(", ApiKey = ***")
            .Append(", Kind = ").Append(Kind);
        return true;
    }

    /// <summary>
    /// Validates.
    /// </summary>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            yield return Validation.NotNullOrWhiteSpace(Name);
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            yield return Validation.NotNullOrWhiteSpace(ApiKey);
        }
    }
}
