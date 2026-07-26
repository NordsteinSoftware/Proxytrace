namespace Proxytrace.Application.Optimization;

/// <summary>
/// Tuning for how an optimization theory is proven before it becomes a proposal. Bound from the
/// <c>Optimization</c> configuration section; the defaults are the ones a real installation should
/// run with.
/// </summary>
public sealed record OptimizationOptions
{
    /// <summary>
    /// Maximum two-sided p-value at which an observed pass-rate difference counts as real rather
    /// than sampling noise.
    /// </summary>
    public double SignificanceLevel { get; init; } = 0.05;

    /// <summary>
    /// Whether a candidate must clear <see cref="SignificanceLevel"/> to win, or whether beating the
    /// baseline is enough. Always true for a real installation: without the gate, a suite of a dozen
    /// cases will hand you a "win" built on two flaky judge calls, and you ship a prompt change that
    /// did nothing.
    ///
    /// The kiosk showcase turns it off — see <see cref="KioskShowcase"/>. The p-value is still
    /// computed and stored either way, and the UI reports when a win was not significance-backed, so
    /// a relaxed gate never gets presented as proof it isn't.
    /// </summary>
    public bool RequireStatisticalSignificance { get; init; } = true;

    /// <summary>
    /// Runs executed per A/B arm (1..<see cref="Domain.TestRunGroup.ITestRunGroup.MaxSampleCount"/>).
    /// Results are pooled across samples, so this multiplies the evidence a theory is judged on.
    ///
    /// One sample per arm caps the evidence at the suite's case count, and on a small suite that is
    /// not enough to clear <see cref="SignificanceLevel"/> for anything but an enormous effect: an
    /// 11-case suite improving 5/11 → 8/11 — a large, real improvement — lands at p≈0.19 and is
    /// rejected as noise. Sampling three times turns the same effect into 15/33 → 24/33, which is
    /// significant. The cost is proportional: each extra sample is another full suite run per arm.
    /// </summary>
    public int AbSampleCount { get; init; } = 3;

    /// <summary>
    /// Settings for the kiosk showcase, where a presenter is standing in front of an audience and
    /// three samples per arm would triple the wait at the demo's slowest step. Kiosk buys speed by
    /// accepting weaker evidence: one sample per arm, and a candidate that merely beats the
    /// baseline wins.
    ///
    /// A single sample caps the evidence at 11 cases, and the demo's effect (measured p = 0.19,
    /// 0.09 and 0.34 across three rehearsals of the same 5/11 → 8/11 improvement) straddles any
    /// fixed threshold — so no threshold makes the step reliably green. Dropping the gate does.
    ///
    /// This is a deliberate trade for a scripted demo on seeded data — never a default for a real
    /// installation, where a false positive means shipping a prompt change that did nothing. The
    /// p-value is still recorded, and the UI labels such a win "not significance-tested".
    /// </summary>
    public static OptimizationOptions KioskShowcase { get; } = new()
    {
        RequireStatisticalSignificance = false,
        AbSampleCount = 1,
    };
}
