using Proxytrace.Domain.Statistics.TestRun;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Api.Dto.TestSuites;

/// <summary>
/// Maps <see cref="ITestSuite"/> domain entities to <see cref="TestSuiteDto"/>
/// and converts request DTOs into domain message/conversation values.
/// </summary>
public sealed class TestSuiteDtoMapper
{
    /// <summary>
    /// Maps a suite with no run history (e.g. immediately after create/update).
    /// </summary>
    public TestSuiteDto ToDto(ITestSuite s) => ToDto(s, []);

    /// <summary>
    /// Maps a suite, deriving run aggregates (total runs, pass rate, trend, last run)
    /// from the finalized <paramref name="runRows"/> belonging to that suite.
    /// </summary>
    public TestSuiteDto ToDto(ITestSuite s, IReadOnlyList<TestRunStats> runRows)
    {
        var agg = RunAggregates.From(runRows);

        return new(
            s.Id,
            s.Name,
            s.Agent.Id,
            s.Agent.Name,
            s.Evaluators.Select(e => new EvaluatorDto(e.Id, e.Kind)).ToArray(),
            s.TestCases.Select(tc => new TestCaseDto(
                tc.Id,
                tc.Input.Messages.Select(ToInputMessageDto).ToArray(),
                ToExpectedOutputDto(tc.ExpectedOutput),
                tc.SourceAgentCallId,
                tc.Input.ResolvedToolCallCount
            )).ToArray(),
            Description: null,
            Tags: [],
            TotalRuns: agg.TotalRuns,
            PassRate: agg.PassRate,
            PrevPassRate: agg.PrevPassRate,
            PassRateTrend: agg.Trend,
            LastRunAt: agg.LastRunAt,
            LastRunGroupId: agg.LastRunGroupId,
            s.CreatedAt,
            s.UpdatedAt);
    }

    /// <summary>
    /// Lightweight projection for the suites grid — keeps evaluator refs + run aggregates but ships
    /// only the test-case count, not the full input conversations. Mirrors <see cref="ToDto"/>.
    /// </summary>
    public TestSuiteListItemDto ToListItemDto(ITestSuite s, IReadOnlyList<TestRunStats> runRows)
    {
        var agg = RunAggregates.From(runRows);

        return new(
            s.Id,
            s.Name,
            s.Agent.Id,
            s.Agent.Name,
            s.Evaluators.Select(e => new EvaluatorDto(e.Id, e.Kind)).ToArray(),
            TestCaseCount: s.TestCases.Count,
            Description: null,
            Tags: [],
            TotalRuns: agg.TotalRuns,
            PassRate: agg.PassRate,
            PrevPassRate: agg.PrevPassRate,
            PassRateTrend: agg.Trend,
            LastRunAt: agg.LastRunAt,
            LastRunGroupId: agg.LastRunGroupId,
            s.CreatedAt,
            s.UpdatedAt);
    }

    /// <summary>
    /// Aggregates a suite's finalized run rows (already filtered to the requested window) into the
    /// bucket stats the suite detail strip shows. PassRate is total-passed / total-cases across the
    /// window (null when no cases ran); AvgDurationMs is the mean of rows that recorded a duration;
    /// TotalCost sums recorded costs. RunCount is the row count.
    /// </summary>
    public SuiteRunStatsDto ToRunStatsDto(IReadOnlyList<TestRunStats> rawRows)
    {
        // Collapse each (group, endpoint) cohort's samples to one row so RunCount and TotalCost
        // reflect endpoints, not the number of samples per endpoint.
        var runRows = rawRows.AggregateSamples();
        if (runRows.Count == 0)
            return new SuiteRunStatsDto(0, null, null, null);

        int totalCases = runRows.Sum(r => r.TestCases);
        int totalPassed = runRows.Sum(r => r.Passed);
        double? passRate = totalCases > 0 ? totalPassed / (double)totalCases * 100 : null;

        double[] durations = runRows
            .Where(r => r.TotalDuration.HasValue)
            .Select(r => r.TotalDuration.GetValueOrDefault().TotalMilliseconds)
            .ToArray();
        double? avgDurationMs = durations.Length > 0 ? durations.Average() : null;

        decimal[] costs = runRows.Where(r => r.Cost.HasValue).Select(r => r.Cost.GetValueOrDefault()).ToArray();
        decimal? totalCost = costs.Length > 0 ? costs.Sum() : null;

        return new SuiteRunStatsDto(runRows.Count, passRate, avgDurationMs, totalCost);
    }

    /// <summary>Run-history aggregates (total runs, latest/prev pass rate, trend, last run) derived
    /// from a suite's finalized run rows. Shared by the fat and light suite projections.</summary>
    private readonly record struct RunAggregates(
        int TotalRuns,
        double? PassRate,
        double? PrevPassRate,
        double[] Trend,
        DateTimeOffset? LastRunAt,
        Guid? LastRunGroupId)
    {
        /// <summary>
        /// From.
        /// </summary>
        public static RunAggregates From(IReadOnlyList<TestRunStats> runRows)
        {
            // One point per (group, endpoint) cohort — a sampled run counts as one run, and its
            // latest/prev/trend points are the cohort means rather than individual samples.
            TestRunStats[] ordered = runRows.AggregateSamples().OrderBy(r => r.RunCompletedAt).ToArray();
            double[] trend = ordered
                .Where(r => r.PassRate.HasValue)
                .Select(r => r.PassRate.GetValueOrDefault() * 100)
                .ToArray();
            TestRunStats? latest = ordered.Length > 0 ? ordered[^1] : null;
            TestRunStats? prev = ordered.Length >= 2 ? ordered[^2] : null;
            return new(ordered.Length, latest?.PassRate * 100, prev?.PassRate * 100, trend, latest?.RunCompletedAt, latest?.GroupId);
        }
    }

    /// <summary>
    /// Builds the conversation.
    /// </summary>
    public Conversation BuildConversation(IReadOnlyList<TestSuiteMessageDto> messages)
    {
        var msgs = new List<Message>();
        foreach (var m in messages)
        {
            Message msg = m.Role.ToLower() switch
            {
                "user" => new UserMessage([Content.FromText(m.Content)]),
                "assistant" => new AssistantMessage([Content.FromText(m.Content)], []),
                "system" => new SystemMessage([Content.FromText(m.Content)]),
                _ => new UserMessage([Content.FromText(m.Content)])
            };
            msgs.Add(msg);
        }
        return new Conversation(msgs);
    }

    /// <summary>
    /// Builds the assistant message.
    /// </summary>
    public AssistantMessage BuildAssistantMessage(TestSuiteMessageDto m)
    {
        IReadOnlyList<Content> contents = string.IsNullOrEmpty(m.Content)
            ? []
            : [Content.FromText(m.Content)];
        IReadOnlyList<ToolRequest> toolRequests = (m.ToolRequests ?? [])
            .Select(tr => new ToolRequest(Guid.NewGuid().ToString(), tr.Name, tr.Arguments))
            .ToArray();
        return new AssistantMessage(contents, toolRequests);
    }

    /// <summary>
    /// Maps an input conversation message into its DTO, preserving tool requests (assistant
    /// turns) and tool-call ids (tool result turns) so the UI can pair a tool call with its
    /// response instead of rendering an orphaned result.
    /// </summary>
    private static TestSuiteMessageDto ToInputMessageDto(Message m)
    {
        switch (m)
        {
            case AssistantMessage assistant:
                IReadOnlyList<ToolRequestInputDto>? requests = assistant.ToolRequests.Count > 0
                    ? assistant.ToolRequests.Select(tr => new ToolRequestInputDto(tr.Name, tr.Arguments, tr.Id)).ToArray()
                    : null;
                return new TestSuiteMessageDto("assistant", assistant.GetText(), requests);
            case ToolMessage tool:
                var (id, contents) = tool.Deconstruct();
                var content = string.Concat(contents.Select(c => c.Text ?? ""));
                return new TestSuiteMessageDto("tool", content, null, id);
            default:
                return new TestSuiteMessageDto(m.Role.ToString().ToLower(), m.GetText());
        }
    }

    /// <summary>
    /// Maps an expected assistant response into its DTO, preserving any tool requests.
    /// </summary>
    public TestSuiteMessageDto ToExpectedOutputDto(AssistantMessage expected)
        => new(
            "assistant",
            expected.GetText(),
            expected.ToolRequests.Count > 0
                ? expected.ToolRequests.Select(tr => new ToolRequestInputDto(tr.Name, tr.Arguments)).ToArray()
                : null);
}
