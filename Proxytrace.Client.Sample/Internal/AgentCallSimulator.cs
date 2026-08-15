using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Proxytrace.Client.Sample.Internal;

/// <summary>
/// Demonstrates ingestion via the Proxytrace OpenAI-compatible reverse proxy.
/// No instrumentation required — just change base_url and every API call is captured.
/// </summary>
internal class AgentCallSimulator
{
    private readonly Configuration configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentCallSimulator"/> class.
    /// </summary>
    public AgentCallSimulator(Configuration configuration)
    {
        this.configuration = configuration;
    }
    
    /// <summary>
    /// Runs.
    /// </summary>
    public async Task Run(CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient();
        httpClient.BaseAddress = new Uri(configuration.Endpoint.TrimEnd('/') + "/");

        var requestPayload = new
        {
            model = "gpt-4o-mini",
            messages = new[]
            {
                new { role = "system", content = "You are a helpful assistant." },
                new { role = "user",   content = "What is the capital of France?" },
            },
            max_tokens = 64,
        };

        var json = JsonSerializer.Serialize(requestPayload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        // Real apps would pass their actual API key here
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", configuration.ApiKey);

        Console.WriteLine("  [proxy] POST /openai/v1/chat/completions via Proxytrace proxy...");

        try
        {
            var response = await httpClient.PostAsync("openai/v1/chat/completions", content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"  [proxy] Status: {(int)response.StatusCode} — {body[..Math.Min(120, body.Length)]}...");
        }
        catch (HttpRequestException ex)
        {
            // Expected when Proxytrace.Api is not running — shows the call was attempted
            Console.WriteLine($"  [proxy] Could not reach Proxytrace.Api ({ex.Message}). Start Proxytrace.Api to test this scenario.");
        }
    }
}
