using Proxytrace.Domain.Scope;

namespace Proxytrace.Application.Demo.Internal;

/// <summary>
/// The kiosk demo's scopes (use-case groups of agents) and which scope each demo agent's traffic is
/// sent under. Shared by the seed (<c>ScopeSeedScenario</c>) and the live traffic generator so the
/// two agree. The Email Triage agent routes every third email (bug reports) to Engineering, so the
/// demo shows one agent serving two scopes.
/// </summary>
internal static class DemoScopes
{
    /// <summary>One demo scope: its canonical key and curated label.</summary>
    internal sealed record Definition(string Key, string DisplayName, string Description);

    internal static readonly Definition CustomerCare = new(
        "customer-care", "Customer care", "Answers customers and triages the support inbox.");

    internal static readonly Definition Engineering = new(
        "engineering", "Engineering", "Reviews pull requests and picks up bug reports from the inbox.");

    internal static readonly Definition Analytics = new(
        "analytics", "Analytics", "Answers data questions over the warehouse.");

    internal static readonly IReadOnlyList<Definition> All = [CustomerCare, Engineering, Analytics];

    /// <summary>The scope the <paramref name="sequence"/>-th call of an agent belongs to, if any.</summary>
    internal static Definition? For(string agentName, long sequence) => agentName switch
    {
        "Customer Support Agent" => CustomerCare,
        "Email Triage Agent" => sequence % 3 == 0 ? Engineering : CustomerCare,
        "Code Review Agent" => Engineering,
        "Data Analytics Agent" => Analytics,
        _ => null,
    };

    /// <summary>Admits the scope in the project and gives it its curated label on first creation.</summary>
    internal static async Task<Guid?> EnsureAsync(
        IScopeRepository scopes, Guid projectId, Definition definition, CancellationToken cancellationToken)
    {
        var scopeId = await scopes.AdmitAsync(projectId, definition.Key, int.MaxValue, cancellationToken);
        if (scopeId is not { } id)
            return null;

        var scope = await scopes.GetAsync(id, cancellationToken);
        if (scope.DisplayName is null)
            await scope.ChangeDetails(definition.DisplayName, definition.Description, cancellationToken);
        return id;
    }
}
