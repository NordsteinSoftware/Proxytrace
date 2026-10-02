using JetBrains.Annotations;
using Proxytrace.Application.Demo.Internal;
using Proxytrace.Domain.Scope;

namespace Proxytrace.Application.Demo.Scenarios;

/// <summary>
/// Creates the demo scopes (<see cref="DemoScopes"/>) right after the project exists and records
/// their ids on the seed context, so the traffic scenarios that follow (the statistics backfill, and
/// the live traffic feed) stamp each call with its scope at creation — exactly as ingestion does.
/// The hand-curated calls of the core seed (Order 0) predate the scopes and stay unscoped, which
/// keeps an "unscoped" bucket in the demo as real projects have.
/// </summary>
[UsedImplicitly]
internal sealed class ScopeSeedScenario : IDemoScenario
{
    private readonly DemoSeedContext ctx;
    private readonly IScopeRepository scopes;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScopeSeedScenario"/> class.
    /// </summary>
    public ScopeSeedScenario(DemoSeedContext ctx, IScopeRepository scopes)
    {
        this.ctx = ctx;
        this.scopes = scopes;
    }

    /// <summary>
    /// Right after the core seed (0) created the project and agents.
    /// </summary>
    public int Order => 1;

    /// <summary>
    /// Seeds asynchronously.
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var projectId = ctx.RequireProject().Id;
        foreach (var definition in DemoScopes.All)
        {
            if (await DemoScopes.EnsureAsync(scopes, projectId, definition, cancellationToken) is { } id)
                ctx.ScopeIds[definition.Key] = id;
        }
    }
}
