using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Autofac;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Inference;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Session;
using Proxytrace.PerfHarness.Bootstrap;
using Proxytrace.PerfHarness.Reporting;

namespace Proxytrace.PerfHarness.Scenarios;

/// <summary>
/// Measures sustained write-ingestion throughput against the already-populated table: many concurrent
/// workers each persist single agent-call rows through the real repository (one <c>SaveChanges</c> per
/// call — the exact per-envelope DB cost the ingestion worker pays). Each worker uses its own lifetime
/// scope, mirroring the worker's per-envelope context isolation. The CPU cost of parsing the OpenAI
/// payload is measured separately by the BenchmarkDotNet micro-benchmarks; this isolates the database
/// write path, which is what degrades as the table grows.
/// </summary>
internal static class IngestionThroughputScenario
{
    // Fraction of ingested calls that carry a session key (and pay the session upsert), matching the
    // ~SessionRate the seeder uses. A small fixed key pool keeps the derived session rows bounded.
    private const double SessionShare = 0.30;
    private const int ProbeSessionPoolSize = 64;

    public static async Task<MetricResult> RunAsync(
        PerfContainer container,
        PerfBudgets budgets,
        long ingestCount,
        int concurrency,
        CancellationToken cancellationToken)
    {
        // Reuse the seeded graph + a small canned-payload pool; build it once.
        var graph = await container.InScopeAsync(async scope =>
        {
            var agentRepo = scope.Resolve<IRepository<IAgent>>();
            var endpointRepo = scope.Resolve<IRepository<IModelEndpoint>>();
            var projectRepo = scope.Resolve<IRepository<IProject>>();
            var conversationGenerator = scope.Resolve<IDomainObjectGenerator<Conversation>>();
            var completionGenerator = scope.Resolve<IDomainObjectGenerator<ICompletion>>();
            var modelParametersGenerator = scope.Resolve<IDomainObjectGenerator<IModelParameters>>();

            var agents = (await agentRepo.GetAllAsync(cancellationToken)).Where(a => !a.IsSystemAgent).ToList();
            var endpoints = (await endpointRepo.GetAllAsync(cancellationToken)).ToList();
            var project = await projectRepo.FindFirstAsync(cancellationToken);
            if (agents.Count == 0 || endpoints.Count == 0 || project is null)
            {
                throw new InvalidOperationException("No seeded agents/endpoints/project found — run `seed` first.");
            }

            var conversations = new List<Conversation>();
            var completions = new List<ICompletion>();
            var modelParameters = new List<IModelParameters>();
            for (int i = 0; i < 5; i++)
            {
                conversations.Add(await conversationGenerator.CreateAsync(cancellationToken));
                completions.Add(await completionGenerator.CreateAsync(cancellationToken));
                modelParameters.Add(await modelParametersGenerator.CreateAsync(cancellationToken));
            }

            // A small fixed pool of probe session keys. ~SessionShare of ingested calls carry one and
            // pay the session upsert (RecordActivityAsync) in the timed section, exactly as the worker
            // does. Fixed keys keep the derived session rows a bounded set that upserts (not grows)
            // across kept-DB iterate runs; they are removed with the probe rows below to restore state.
            var probeSessions = Enumerable.Range(0, ProbeSessionPoolSize)
                .Select(i =>
                {
                    string key = $"perf-probe-session-{i}";
                    return new ProbeSession(SessionIdDerivation.Derive(project.Id, key), key);
                })
                .ToArray();

            return new IngestGraph(agents, endpoints, conversations, completions, modelParameters, project.Id, probeSessions);
        });

        long perWorker = Math.Max(1, ingestCount / concurrency);
        long planned = perWorker * concurrency;
        Console.WriteLine($"[ingestion] inserting {planned:N0} calls across {concurrency} workers…");

        // Capture the id of every row this probe inserts so it can delete exactly those rows once the
        // measurement is recorded (issue #294). Concurrent because the workers insert in parallel.
        var insertedIds = new ConcurrentBag<Guid>();

        var stopwatch = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, concurrency).Select(workerIndex => Task.Run(async () =>
        {
            var rng = new Random(1000 + workerIndex);
            for (long i = 0; i < perWorker; i++)
            {
                ProbeSession? session = rng.NextDouble() < SessionShare
                    ? graph.ProbeSessions[rng.Next(graph.ProbeSessions.Count)]
                    : null;

                await container.InScopeAsync(async scope =>
                {
                    var createNew = scope.Resolve<IAgentCall.CreateNew>();
                    var repository = scope.Resolve<IAgentCallRepository>();

                    var agent = graph.Agents[rng.Next(graph.Agents.Count)];
                    var endpoint = graph.Endpoints[rng.Next(graph.Endpoints.Count)];
                    var call = createNew(
                        agent: agent,
                        version: agent.CurrentVersion,
                        endpoint: endpoint,
                        request: graph.Conversations[rng.Next(graph.Conversations.Count)],
                        response: graph.Completions[rng.Next(graph.Completions.Count)],
                        httpStatus: HttpStatusCode.OK,
                        finishReason: "stop",
                        errorMessage: null,
                        modelParameters: graph.ModelParameters[rng.Next(graph.ModelParameters.Count)],
                        conversationId: null,
                        sessionId: session?.Id);

                    await repository.AddAsync(call, cancellationToken);
                    insertedIds.Add(call.Id);

                    // Mirror the worker: after the call persists, upsert the session (bump activity /
                    // counters). This is part of the per-envelope DB cost when a session key is present,
                    // so it belongs inside the timed section.
                    if (session is { } s)
                    {
                        long totalTokens = call.Response?.Usage is { } u
                            ? (long)(u.InputTokenCount + u.OutputTokenCount)
                            : 0;
                        var sessionRepository = scope.Resolve<ISessionRepository>();
                        await sessionRepository.RecordActivityAsync(
                            s.Id, s.ExternalKey, graph.ProjectId, totalTokens, call.CreatedAt, cancellationToken);
                    }
                });
            }
        }, cancellationToken));

        await Task.WhenAll(workers);
        stopwatch.Stop();

        double callsPerSec = planned / stopwatch.Elapsed.TotalSeconds;
        Console.WriteLine($"[ingestion] {planned:N0} calls in {stopwatch.Elapsed.TotalSeconds:N1}s = {callsPerSec:N0} calls/s");

        double? budget = budgets.Ingestion.CallsPerSecMin > 0 ? budgets.Ingestion.CallsPerSecMin : null;
        var result = new MetricResult("ingestion", "ingestThroughput", callsPerSec, budget, "calls/s", BudgetDirection.HigherIsBetter);

        // Remove exactly the rows this probe inserted, OUTSIDE the timed section — the throughput
        // number is already captured in `callsPerSec`, so cleanup cannot distort it. Every probe row is
        // stamped with a wall-clock `CreatedAt = now`, landing in the only measured window small enough
        // to notice it: statsPulse's trailing 60 minutes. Left behind, the rows make the documented
        // "seed once, iterate db-layer against a kept DB" workflow history-dependent — they accumulate
        // and inflate statsPulse ~0.7ms per 1k rows until they age out after an hour (issue #294).
        // Deleting them restores the post-seed state for the next iterate run; the AgentCall→tool FK
        // cascades, so each parent's child tool rows go with it.
        await CleanupProbeRowsAsync(container, insertedIds, concurrency, cancellationToken);

        // Also drop the probe session rows this run's upserts created/touched, so the recent-sessions
        // list (sessionsRecent) measures the post-seed state on the next kept-DB iterate run rather than
        // a set of freshly-timestamped probe sessions with no surviving traces. The pool is fixed and
        // small, so deleting every derived id is cheap and idempotent.
        await CleanupProbeSessionsAsync(container, graph.ProbeSessions, cancellationToken);

        return result;
    }

    /// <summary>
    /// Hard-deletes the probe's inserted rows, fanned out across the same number of workers as the
    /// insert loop so cleanup stays quick. Runs only after the throughput measurement is captured, so
    /// it never affects the reported number.
    /// </summary>
    private static async Task CleanupProbeRowsAsync(
        PerfContainer container,
        IReadOnlyCollection<Guid> insertedIds,
        int concurrency,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"[ingestion] removing {insertedIds.Count:N0} probe rows (restoring post-seed state)…");

        var ids = insertedIds.ToArray();
        var workers = Enumerable.Range(0, concurrency).Select(workerIndex => Task.Run(async () =>
        {
            for (int i = workerIndex; i < ids.Length; i += concurrency)
            {
                Guid id = ids[i];
                await container.InScopeAsync(async scope =>
                {
                    var repository = scope.Resolve<IAgentCallRepository>();
                    await repository.RemoveAsync(id, cancellationToken);
                });
            }
        }, cancellationToken));

        await Task.WhenAll(workers);
    }

    /// <summary>
    /// Removes the probe session rows the timed loop upserted. Best-effort and outside the timed
    /// section, so it never affects the reported throughput; a session with no rows simply no-ops.
    /// </summary>
    private static async Task CleanupProbeSessionsAsync(
        PerfContainer container,
        IReadOnlyCollection<ProbeSession> probeSessions,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"[ingestion] removing up to {probeSessions.Count:N0} probe session rows…");
        foreach (ProbeSession session in probeSessions)
        {
            await container.InScopeAsync(async scope =>
            {
                var repository = scope.Resolve<ISessionRepository>();
                await repository.RemoveAsync(session.Id, cancellationToken);
            });
        }
    }

    private sealed record IngestGraph(
        IReadOnlyList<IAgent> Agents,
        IReadOnlyList<IModelEndpoint> Endpoints,
        IReadOnlyList<Conversation> Conversations,
        IReadOnlyList<ICompletion> Completions,
        IReadOnlyList<IModelParameters> ModelParameters,
        Guid ProjectId,
        IReadOnlyList<ProbeSession> ProbeSessions);

    private readonly record struct ProbeSession(Guid Id, string ExternalKey);
}
