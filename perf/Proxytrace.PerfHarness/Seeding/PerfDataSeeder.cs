using System.Diagnostics;
using System.Net;
using Autofac;
using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.Inference;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.Model;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Session;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Domain.Usage;
using Proxytrace.PerfHarness.Bootstrap;

namespace Proxytrace.PerfHarness.Seeding;

/// <summary>
/// Bulk-loads a realistic dataset into the perf Postgres database. A small fixed graph (one project,
/// a handful of endpoints, dozens of agents) is created through the production generators, then the
/// high-volume <see cref="IAgentCall"/> rows are built via the <see cref="IAgentCall.CreateExisting"/>
/// factory — which lets us stamp a controlled <c>CreatedAt</c> (the mapper copies it verbatim) so the
/// rows spread across a realistic time window — and inserted in batches through the real repository.
/// </summary>
internal sealed class PerfDataSeeder
{
    private readonly PerfContainer container;

    public PerfDataSeeder(PerfContainer container)
    {
        this.container = container;
    }

    public async Task<SeedSummary> SeedAsync(SeedOptions options, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var rng = new Random(options.RandomSeed);

        // --- 1. fixed seed graph + canned payload pools (one scope) ---
        Console.WriteLine($"[seed] building fixed graph: 1 project, {options.EndpointCount} endpoints, {options.AgentCount} agents…");
        var graph = await BuildGraphAsync(options, cancellationToken);

        // --- 2. high-volume agent-call rows, batched ---
        var start = DateTimeOffset.UtcNow.AddDays(-options.DaysSpread);
        var span = TimeSpan.FromDays(options.DaysSpread);

        // Pool of conversation ids so ~ConversationRate of calls cluster into multi-turn threads.
        int conversationPoolSize = Math.Max(1, (int)(options.TargetCalls * options.ConversationRate / 3));
        var conversationIds = Enumerable.Range(0, conversationPoolSize).Select(_ => Guid.NewGuid()).ToArray();

        // Pool of debugging sessions so ~SessionRate of calls carry a SessionId. The id is derived the
        // same way ingestion derives it (SessionIdDerivation.Derive over the project + external key), so
        // the trace's stamped SessionId and the Sessions row we insert below agree. Pool sized like the
        // conversation pool (~TargetCalls * SessionRate / 3) for comparable per-session cardinality.
        int sessionPoolSize = Math.Max(1, (int)(options.TargetCalls * options.SessionRate / 3));
        var sessionPool = new SessionSeed[sessionPoolSize];
        for (int i = 0; i < sessionPoolSize; i++)
        {
            string externalKey = $"perf-session-{i}";
            sessionPool[i] = new SessionSeed(SessionIdDerivation.Derive(graph.ProjectId, externalKey), externalKey);
        }

        // Denormalized counters accumulated as calls are assigned to sessions, mirroring what the
        // ingestion upsert (RecordActivityAsync) maintains. TraceCount/TotalTokens are exact for the
        // seeded rows; LastActivityAt is the newest member call's CreatedAt. (Approximate only in that
        // sessions never picked below are skipped — the perf DB only needs realistic cardinality.)
        var sessionAccumulators = new SessionAccumulator[sessionPoolSize];

        long inserted = 0;
        while (inserted < options.TargetCalls)
        {
            int batchCount = (int)Math.Min(options.BatchSize, options.TargetCalls - inserted);

            await container.InScopeAsync(async scope =>
            {
                var createExisting = scope.Resolve<IAgentCall.CreateExisting>();
                var createCompletion = scope.Resolve<ICompletion.Create>();
                var repository = scope.Resolve<IAgentCallRepository>();

                var batch = new List<IAgentCall>(batchCount);
                for (int i = 0; i < batchCount; i++)
                {
                    batch.Add(BuildCall(graph, conversationIds, sessionPool, sessionAccumulators,
                        createExisting, createCompletion, rng, start, span, options));
                }

                await repository.AddRangeAsync(batch, cancellationToken);
            });

            inserted += batchCount;
            Console.WriteLine($"[seed] {inserted:N0}/{options.TargetCalls:N0} calls ({stopwatch.Elapsed:hh\\:mm\\:ss})");
        }

        // Insert the Sessions rows once, after the call loop, for every pool entry that actually received
        // a call — with the counters accumulated above, so session lists never aggregate the traces table.
        await SeedSessionsAsync(graph, sessionPool, sessionAccumulators, cancellationToken);

        // --- 3. per-run test-run statistics rows (for the suite-scoped TestRunStats query, #253) ---
        await SeedTestRunStatsAsync(graph, options, cancellationToken);

        stopwatch.Stop();
        Console.WriteLine($"[seed] done: {inserted:N0} calls in {stopwatch.Elapsed:hh\\:mm\\:ss}");

        return new SeedSummary(
            ProjectId: graph.ProjectId,
            ProviderId: graph.ProviderId,
            AgentIds: graph.Agents.Select(a => a.Id).ToArray(),
            EndpointIds: graph.Endpoints.Select(e => e.Id).ToArray(),
            CallsInserted: inserted,
            Elapsed: stopwatch.Elapsed);
    }

    /// <summary>
    /// Bulk-inserts one <c>Sessions</c> row per pool entry that received at least one call, carrying the
    /// counters accumulated during the call loop (the same denormalized shape the ingestion upsert
    /// maintains). The id is the one already stamped on the member traces — derived from
    /// (project, external key) — so the session-filtered traces list and the recent-sessions list both
    /// hit real, non-empty sessions. Rows go in through the real <see cref="ISessionRepository"/> in
    /// batches, matching how the high-volume call rows are inserted.
    /// </summary>
    private async Task SeedSessionsAsync(
        SeedGraph graph,
        SessionSeed[] sessionPool,
        SessionAccumulator[] sessionAccumulators,
        CancellationToken cancellationToken)
    {
        var populated = Enumerable.Range(0, sessionPool.Length)
            .Where(i => sessionAccumulators[i].TraceCount > 0)
            .ToArray();
        if (populated.Length == 0)
        {
            return;
        }

        Console.WriteLine($"[seed] building {populated.Length:N0} session rows…");

        int batchSize = 10_000;
        for (int offset = 0; offset < populated.Length; offset += batchSize)
        {
            int end = Math.Min(offset + batchSize, populated.Length);
            await container.InScopeAsync(async scope =>
            {
                var createExisting = scope.Resolve<ISession.CreateExisting>();
                var repository = scope.Resolve<ISessionRepository>();

                var batch = new List<ISession>(end - offset);
                for (int i = offset; i < end; i++)
                {
                    int idx = populated[i];
                    SessionSeed seed = sessionPool[idx];
                    SessionAccumulator acc = sessionAccumulators[idx];
                    var data = new SeedEntityData(seed.Id, acc.LastActivityAt, acc.LastActivityAt);
                    batch.Add(createExisting(
                        externalKey: seed.ExternalKey,
                        projectId: graph.ProjectId,
                        lastActivityAt: acc.LastActivityAt,
                        traceCount: acc.TraceCount,
                        totalTokens: acc.TotalTokens,
                        existing: data));
                }

                await repository.AddRangeAsync(batch, cancellationToken);
            });
        }
    }

    private async Task<SeedGraph> BuildGraphAsync(SeedOptions options, CancellationToken cancellationToken)
    {
        return await container.InScopeAsync(async scope =>
        {
            var projectGenerator = scope.Resolve<IDomainEntityGenerator<IProject>>();
            var providerGenerator = scope.Resolve<IDomainEntityGenerator<IModelProvider>>();
            var modelGenerator = scope.Resolve<IDomainEntityGenerator<IModel>>();
            var endpointFactory = scope.Resolve<IModelEndpoint.CreateNew>();
            var endpointRepository = scope.Resolve<IRepository<IModelEndpoint>>();
            var agentGenerator = scope.Resolve<IAgentGenerator>();
            var conversationGenerator = scope.Resolve<IDomainObjectGenerator<Conversation>>();
            var completionGenerator = scope.Resolve<IDomainObjectGenerator<ICompletion>>();
            var modelParametersGenerator = scope.Resolve<IDomainObjectGenerator<IModelParameters>>();

            var project = await projectGenerator.GetOrCreateAsync(cancellationToken);

            // One shared provider with N distinct models — ModelEndpoint is unique per (model, provider),
            // so each endpoint needs its own model rather than reusing the generator's GetOrCreate pair.
            var provider = await providerGenerator.CreateAsync(cancellationToken);
            var endpoints = new List<IModelEndpoint>(options.EndpointCount);
            for (int i = 0; i < options.EndpointCount; i++)
            {
                var model = await modelGenerator.CreateAsync(cancellationToken);
                var endpoint = endpointFactory(model, provider, 0.000001m, 0.000003m, 0.0000005m);
                endpoints.Add(await endpointRepository.AddAsync(endpoint, cancellationToken));
            }

            // A distinct system prompt per agent keeps each initial version's fingerprint unique
            // (AgentVersion is unique per (Project, Fingerprint), which a shared generated prompt breaks).
            var agents = new List<IAgent>(options.AgentCount);
            for (int i = 0; i < options.AgentCount; i++)
            {
                agents.Add(await agentGenerator.CreateAsync(
                    $"Perf Agent {i:D3}",
                    systemPrompt: $"You are perf test agent number {i}. Answer concisely.",
                    cancellationToken: cancellationToken));
            }

            // Canned payload pools reused across all rows. Token usage and latency are varied per row
            // at build time; the conversation/assistant-message shapes come from a small fixed pool.
            var conversations = new List<Conversation>();
            for (int i = 0; i < 10; i++)
            {
                conversations.Add(await conversationGenerator.CreateAsync(cancellationToken));
            }

            var assistantMessages = new List<AssistantMessage>();
            for (int i = 0; i < 5; i++)
            {
                assistantMessages.Add((await completionGenerator.CreateAsync(cancellationToken)).Response);
            }

            // A fixed set of named tools so a realistic fraction (~3/8 of successful calls) carries
            // tool-request rows — these back the ToolName filter's EXISTS semi-join and the distinct
            // tool-name picker perf paths. Canned names keep the distinct set bounded like real traffic.
            string[][] toolSets =
            [
                ["web_search"],
                ["run_sql", "get_schema"],
                ["lookup_order", "start_return"],
            ];
            foreach (string[] toolSet in toolSets)
            {
                var requests = toolSet.Select((name, i) => new ToolRequest($"tr{i}", name, "{}")).ToList();
                assistantMessages.Add(new AssistantMessage([Content.FromText("Calling tools to answer…")], requests));
            }

            var modelParameters = new List<IModelParameters>();
            for (int i = 0; i < 5; i++)
            {
                modelParameters.Add(await modelParametersGenerator.CreateAsync(cancellationToken));
            }

            return new SeedGraph(project.Id, provider.Id, agents, endpoints, conversations, assistantMessages, modelParameters);
        });
    }

    private static IAgentCall BuildCall(
        SeedGraph graph,
        Guid[] conversationIds,
        SessionSeed[] sessionPool,
        SessionAccumulator[] sessionAccumulators,
        IAgentCall.CreateExisting createExisting,
        ICompletion.Create createCompletion,
        Random rng,
        DateTimeOffset start,
        TimeSpan span,
        SeedOptions options)
    {
        IAgent agent = graph.Agents[rng.Next(graph.Agents.Count)];
        IAgentVersion version = agent.CurrentVersion;
        IModelEndpoint endpoint = graph.Endpoints[rng.Next(graph.Endpoints.Count)];
        Conversation request = graph.Conversations[rng.Next(graph.Conversations.Count)];
        IModelParameters modelParameters = graph.ModelParameters[rng.Next(graph.ModelParameters.Count)];

        DateTimeOffset createdAt = start + span * rng.NextDouble();
        Guid? conversationId = rng.NextDouble() < options.ConversationRate
            ? conversationIds[rng.Next(conversationIds.Length)]
            : null;

        int? sessionIndex = rng.NextDouble() < options.SessionRate ? rng.Next(sessionPool.Length) : null;
        Guid? sessionId = sessionIndex is { } si ? sessionPool[si].Id : null;

        bool isError = rng.NextDouble() < options.ErrorRate;

        ICompletion? response;
        HttpStatusCode status;
        string? finishReason;
        string? errorMessage;
        long callTokens;

        if (isError)
        {
            response = null;
            status = rng.NextDouble() < 0.5 ? HttpStatusCode.InternalServerError : HttpStatusCode.TooManyRequests;
            finishReason = null;
            errorMessage = "Upstream provider error";
            callTokens = 0;
        }
        else
        {
            ulong input = (ulong)rng.Next(200, 4000);
            ulong output = (ulong)rng.Next(50, 1500);
            ulong cached = (ulong)(input * rng.NextDouble() * 0.5);
            var usage = new TokenUsage(input, output, cached);
            var latency = TimeSpan.FromMilliseconds(rng.Next(150, 6000));
            AssistantMessage message = graph.AssistantMessages[rng.Next(graph.AssistantMessages.Count)];
            response = createCompletion(message, usage, latency);
            status = HttpStatusCode.OK;
            finishReason = "stop";
            errorMessage = null;
            // Mirrors the TotalTokens the ingestion upsert denormalizes onto the session (input+output).
            callTokens = (long)(input + output);
        }

        if (sessionIndex is { } idx)
        {
            ref SessionAccumulator acc = ref sessionAccumulators[idx];
            acc.TraceCount++;
            acc.TotalTokens += callTokens;
            if (createdAt > acc.LastActivityAt)
            {
                acc.LastActivityAt = createdAt;
            }
        }

        // ~OutlierRate of calls carry outlier flags: one random statistical bit (occasionally two),
        // and a quarter of flagged rows also carry the async CustomAnomaly bit so both the static and
        // custom count paths of the anomaly aggregates are exercised.
        OutlierFlags outlierFlags = OutlierFlags.None;
        if (rng.NextDouble() < options.OutlierRate)
        {
            outlierFlags = (OutlierFlags)(1 << rng.Next(4));
            if (rng.NextDouble() < 0.2)
            {
                outlierFlags |= (OutlierFlags)(1 << rng.Next(4));
            }
            if (rng.NextDouble() < 0.25)
            {
                outlierFlags |= OutlierFlags.CustomAnomaly;
            }
        }

        var data = new SeedEntityData(Guid.NewGuid(), createdAt, createdAt);
        return createExisting(
            agent: agent,
            version: version,
            endpoint: endpoint,
            request: request,
            response: response,
            httpStatus: status,
            finishReason: finishReason,
            errorMessage: errorMessage,
            modelParameters: modelParameters,
            existing: data,
            conversationId: conversationId,
            sessionId: sessionId,
            outlierFlags: outlierFlags);
    }

    /// <summary>
    /// Seeds <see cref="SeedOptions.TestRunCount"/> per-run statistics rows spread across
    /// <see cref="SeedOptions.TestRunSuitePoolSize"/> synthetic suites, so the suite-scoped
    /// TestRunStats query (<c>WHERE SuiteId IN (...)</c>, issue #253) can be measured against a large,
    /// realistically-distributed table. Each <c>TestRunStatsEntity</c> requires a matching
    /// <c>TestRunEntity</c> (the <c>TestRunId</c> FK is 1:1), so one real anchor suite/group is built
    /// and a test run is inserted per stats row; the stats <c>SuiteId</c> is a plain indexed column
    /// (no FK), so the suite spread is synthetic and needs no extra suite graph.
    /// </summary>
    private async Task SeedTestRunStatsAsync(SeedGraph graph, SeedOptions options, CancellationToken cancellationToken)
    {
        if (options.TestRunCount <= 0)
        {
            return;
        }

        int poolSize = Math.Max(1, options.TestRunSuitePoolSize);
        Console.WriteLine($"[seed] building {options.TestRunCount:N0} test-run stats rows across {poolSize} suites…");
        var rng = new Random(options.RandomSeed + 1);
        var suiteIdPool = Enumerable.Range(0, poolSize).Select(_ => Guid.NewGuid()).ToArray();

        var anchor = await BuildTestRunAnchorAsync(graph, cancellationToken);

        var start = DateTimeOffset.UtcNow.AddDays(-options.DaysSpread);
        var span = TimeSpan.FromDays(options.DaysSpread);

        long created = 0;
        int sampleIndex = 0;
        while (created < options.TestRunCount)
        {
            int batchCount = (int)Math.Min(options.BatchSize, options.TestRunCount - created);

            await container.InScopeAsync(async scope =>
            {
                var createRun = scope.Resolve<ITestRun.CreateNew>();
                var runRepository = scope.Resolve<IRepository<ITestRun>>();
                var statsWriter = scope.Resolve<IStatsWriter<TestRunStats>>();

                var runs = new List<ITestRun>(batchCount);
                for (int i = 0; i < batchCount; i++)
                {
                    IModelEndpoint endpoint = graph.Endpoints[rng.Next(graph.Endpoints.Count)];
                    runs.Add(createRun(anchor.Group, endpoint, sampleIndex++));
                }

                // Test runs first (committed) so the TestRunStats TestRunId FK resolves on upsert.
                await runRepository.AddRangeAsync(runs, cancellationToken);

                foreach (ITestRun run in runs)
                {
                    Guid suiteId = suiteIdPool[rng.Next(suiteIdPool.Length)];
                    await statsWriter.UpsertAsync(BuildRunStats(run, anchor, suiteId, rng, start, span), cancellationToken);
                }
            });

            created += batchCount;
            Console.WriteLine($"[seed] {created:N0}/{options.TestRunCount:N0} test-run stats rows");
        }
    }

    /// <summary>
    /// Builds the single real suite/group the seeded test runs hang off — the minimum graph that
    /// satisfies the <c>TestRunStatsEntity.TestRunId → TestRunEntity</c> FK (one suite with one
    /// evaluator and one test case, plus one run group). Reuses a seeded agent/endpoint and the canned
    /// conversation/assistant-message pools.
    /// </summary>
    private Task<TestRunAnchor> BuildTestRunAnchorAsync(SeedGraph graph, CancellationToken cancellationToken)
        => container.InScopeAsync(async scope =>
        {
            var projectRepository = scope.Resolve<IRepository<IProject>>();
            var evaluatorRepository = scope.Resolve<IRepository<IEvaluator>>();
            var testCaseRepository = scope.Resolve<IRepository<ITestCase>>();
            var suiteRepository = scope.Resolve<IRepository<ITestSuite>>();
            var groupRepository = scope.Resolve<IRepository<ITestRunGroup>>();
            var createEvaluator = scope.Resolve<IExactMatchEvaluator.CreateNew>();
            var createTestCase = scope.Resolve<ITestCase.CreateNew>();
            var createSuite = scope.Resolve<ITestSuite.CreateNew>();
            var createGroup = scope.Resolve<ITestRunGroup.CreateNew>();

            IProject project = await projectRepository.GetAsync(graph.ProjectId, cancellationToken);
            IAgent agent = graph.Agents[0];

            IEvaluator evaluator = await evaluatorRepository.AddAsync(createEvaluator(project), cancellationToken);
            ITestCase testCase = await testCaseRepository.AddAsync(
                createTestCase(graph.Conversations[0], graph.AssistantMessages[0], sourceAgentCallId: null), cancellationToken);
            ITestSuite suite = await suiteRepository.AddAsync(
                createSuite("Perf Run-Stats Suite", agent, [evaluator], [testCase]), cancellationToken);
            ITestRunGroup group = await groupRepository.AddAsync(
                createGroup(suite, isSystemRun: false, null, sampleCount: 1), cancellationToken);

            return new TestRunAnchor(agent.Id, group);
        });

    private static TestRunStats BuildRunStats(
        ITestRun run,
        TestRunAnchor anchor,
        Guid suiteId,
        Random rng,
        DateTimeOffset start,
        TimeSpan span)
    {
        int testCases = rng.Next(3, 25);
        int passed = rng.Next(0, testCases + 1);
        ulong input = (ulong)rng.Next(200, 4000);
        ulong output = (ulong)rng.Next(50, 1500);
        var usage = new TokenUsage(input, output, (ulong)(input * 0.2));
        var duration = TimeSpan.FromMilliseconds(rng.Next(500, 30_000));
        DateTimeOffset completedAt = start + span * rng.NextDouble();

        return new TestRunStats(
            TestRunId: run.Id,
            AgentId: anchor.AgentId,
            EndpointId: run.Endpoint.Id,
            GroupId: anchor.Group.Id,
            SuiteId: suiteId,
            TestCases: testCases,
            Passed: passed,
            TotalDuration: duration,
            Usage: usage,
            Cost: (decimal)(rng.NextDouble() * 0.5),
            RunCompletedAt: completedAt);
    }

    private sealed record SeedGraph(
        Guid ProjectId,
        Guid ProviderId,
        IReadOnlyList<IAgent> Agents,
        IReadOnlyList<IModelEndpoint> Endpoints,
        IReadOnlyList<Conversation> Conversations,
        IReadOnlyList<AssistantMessage> AssistantMessages,
        IReadOnlyList<IModelParameters> ModelParameters);

    private sealed record TestRunAnchor(Guid AgentId, ITestRunGroup Group);

    /// <summary>One pooled debugging session: its derived id and the external key stored on the row.</summary>
    private readonly record struct SessionSeed(Guid Id, string ExternalKey);

    /// <summary>Denormalized counters accumulated for a pooled session as calls are assigned to it.</summary>
    private struct SessionAccumulator
    {
        public int TraceCount;
        public long TotalTokens;
        public DateTimeOffset LastActivityAt;
    }

    private sealed record SeedEntityData(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt) : IDomainEntityData;
}
