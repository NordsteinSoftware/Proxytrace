using System.Globalization;
using Proxytrace.Domain.Notification;

namespace Proxytrace.Application.Anomaly.Internal;

internal sealed class AnomalyDetector : IAnomalyDetector
{
    private readonly AnomalyDetectionConfiguration configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnomalyDetector"/> class.
    /// </summary>
    public AnomalyDetector(AnomalyDetectionConfiguration configuration)
    {
        this.configuration = configuration;
    }

    /// <summary>
    /// Detects.
    /// </summary>
    public IReadOnlyList<DetectedAnomaly> Detect(AnomalyInput input)
    {
        // Rule 1 (hard): the group failed, a run failed, or a run produced no results at all — the
        // last is the common shape of an unavailable endpoint (every case errors and is skipped, so
        // the run never completes and no stats are projected). No baseline required; highest severity.
        var downRuns = input.Runs
            .Where(r => r.RunFailed || (r.TestCaseCount > 0 && r.ResultCount == 0))
            .Select(r => r.EndpointName)
            .ToList();
        if (input.GroupFailed || downRuns.Count > 0)
        {
            var detail = downRuns.Count > 0
                ? $" Affected endpoint(s) (no results — endpoint may be unavailable): {string.Join(", ", downRuns)}."
                : string.Empty;
            return
            [
                new DetectedAnomaly(
                    NotificationSeverity.Critical,
                    $"Test run failed: {input.SuiteName}",
                    $"The test run for suite '{input.SuiteName}' failed — the endpoint may be unavailable.{detail}",
                    NotificationTargetKind.TestRunGroup,
                    input.GroupId),
            ];
        }

        // Rules 2 & 3 (comparison): need a baseline. Evaluate every run, keep the worst of each issue.
        AnomalyRunInput? worstPassRate = null;
        double worstPassRateDrop = 0;
        AnomalyRunInput? worstLatency = null;
        double worstLatencyFactor = 0;

        foreach (var run in input.Runs)
        {
            if (run.BaselineSampleCount < configuration.MinBaselineSamples)
                continue;

            if (run.CurrentPassRate is { } current && run.BaselinePassRate is { } baseline)
            {
                var drop = baseline - current;
                if (drop >= configuration.PassRateDropPoints && drop > worstPassRateDrop)
                {
                    worstPassRateDrop = drop;
                    worstPassRate = run;
                }
            }

            if (run.CurrentAverageLatency is { } curLatency
                && run.BaselineAverageLatency is { TotalMilliseconds: > 0 } baseLatency)
            {
                var factor = curLatency.TotalMilliseconds / baseLatency.TotalMilliseconds;
                if (factor >= configuration.LatencyIncreaseFactor && factor > worstLatencyFactor)
                {
                    worstLatencyFactor = factor;
                    worstLatency = run;
                }
            }
        }

        if (worstPassRate is null && worstLatency is null)
            return [];

        var severity = NotificationSeverity.Warning;
        var parts = new List<string>();

        if (worstPassRate is not null)
        {
            if (worstPassRateDrop >= configuration.PassRateDropCriticalPoints)
                severity = NotificationSeverity.Critical;

            parts.Add(
                $"pass rate on '{worstPassRate.EndpointName}' dropped {FormatPoints(worstPassRateDrop)} " +
                $"(from {FormatPercent(worstPassRate.BaselinePassRate)} to {FormatPercent(worstPassRate.CurrentPassRate)})");
        }

        if (worstLatency is not null)
        {
            parts.Add(
                $"average latency on '{worstLatency.EndpointName}' rose to {worstLatencyFactor.ToString("0.0", CultureInfo.InvariantCulture)}× baseline");
        }

        return
        [
            new DetectedAnomaly(
                severity,
                $"Performance regression: {input.SuiteName}",
                $"In suite '{input.SuiteName}', {string.Join("; ", parts)}.",
                NotificationTargetKind.TestRunGroup,
                input.GroupId),
        ];
    }

    private static string FormatPoints(double fraction)
        => $"{(fraction * 100).ToString("0", CultureInfo.InvariantCulture)} points";

    private static string FormatPercent(double? fraction)
        => fraction is { } f
            ? $"{(f * 100).ToString("0", CultureInfo.InvariantCulture)}%"
            : "n/a";
}
