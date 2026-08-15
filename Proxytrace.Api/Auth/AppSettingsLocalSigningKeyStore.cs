using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;

namespace Proxytrace.Api.Auth;

/// <summary>
/// Persists the generated signing key into appsettings.local.json under
/// Authentication:Local:SigningKey, merging into any existing local settings
/// so unrelated keys are preserved.
/// </summary>
internal sealed class AppSettingsLocalSigningKeyStore : ISigningKeyStore
{
    private const string FileName = "appsettings.local.json";

    private readonly IHostEnvironment environment;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppSettingsLocalSigningKeyStore"/> class.
    /// </summary>
    public AppSettingsLocalSigningKeyStore(IHostEnvironment environment)
    {
        this.environment = environment;
    }

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Reads <c>Authentication:Local:SigningKey</c> from <c>appsettings.local.json</c> in the
    /// content root, returning <see langword="null"/> when the file is absent or unparseable.
    /// </summary>
    public string? Load()
    {
        var path = Path.Combine(environment.ContentRootPath, FileName);
        if (!File.Exists(path))
            return null;

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: ParseOptions) as JsonObject;
            return root?["Authentication"]?["Local"]?["SigningKey"]?.GetValue<string>();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes <paramref name="signingKey"/> into <c>appsettings.local.json</c> under
    /// <c>Authentication:Local:SigningKey</c>, merging into the existing JSON so unrelated
    /// configuration keys are not overwritten.
    /// </summary>
    public void Persist(string signingKey)
    {
        var path = Path.Combine(environment.ContentRootPath, FileName);

        JsonObject root = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path), documentOptions: ParseOptions) as JsonObject ?? new JsonObject()
            : new JsonObject();

        var authentication = root["Authentication"] as JsonObject ?? new JsonObject();
        var local = authentication["Local"] as JsonObject ?? new JsonObject();
        local["SigningKey"] = signingKey;
        authentication["Local"] = local;
        root["Authentication"] = authentication;

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
