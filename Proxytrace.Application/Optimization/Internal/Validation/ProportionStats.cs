namespace Proxytrace.Application.Optimization.Internal.Validation;

/// <summary>
/// Small, dependency-free statistics used to judge whether an observed A/B pass-rate
/// difference is meaningful or just sampling noise.
/// </summary>
internal static class ProportionStats
{
    /// <summary>
    /// Two-sided p-value of a two-proportion z-test comparing a baseline run
    /// (<paramref name="baselinePasses"/> of <paramref name="baselineTotal"/>) against a candidate
    /// run (<paramref name="candidatePasses"/> of <paramref name="candidateTotal"/>). Returns null
    /// when either sample is empty or the pooled variance is zero (both runs identical), where a
    /// p-value is undefined.
    /// </summary>
    public static double? TwoSidedPValue(
        int baselinePasses,
        int baselineTotal,
        int candidatePasses,
        int candidateTotal)
    {
        if (baselineTotal <= 0 || candidateTotal <= 0)
            return null;

        double pBaseline = baselinePasses / (double)baselineTotal;
        double pCandidate = candidatePasses / (double)candidateTotal;
        double pPooled = (baselinePasses + candidatePasses) / (double)(baselineTotal + candidateTotal);

        double variance = pPooled * (1 - pPooled) * (1.0 / baselineTotal + 1.0 / candidateTotal);
        if (variance <= 0)
            return null;

        double z = (pCandidate - pBaseline) / Math.Sqrt(variance);
        double p = 2 * (1 - StandardNormalCdf(Math.Abs(z)));

        // Clamp away tiny floating-point excursions outside the valid [0, 1] range.
        return Math.Clamp(p, 0, 1);
    }

    /// <summary>
    /// Two-sided p-value for an A/B comparison in which both arms ran the <b>same test cases</b>,
    /// each possibly replayed several times. <paramref name="baselinePerCase"/> and
    /// <paramref name="candidatePerCase"/> hold one pass proportion per test case, in matching order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaces a two-proportion test over <b>pooled</b> replays, which was wrong in a way that
    /// always erred toward accepting: summing the samples treated <c>cases × replays</c> as that many
    /// independent trials. Replays of the same case are not independent of each other — a case that
    /// deterministically passes contributes the same outcome every time — so the sample size was
    /// inflated by the replay count and the standard error shrank by roughly √(replays). The gate
    /// therefore admitted differences that were not significant, and admitted them more readily the
    /// more samples an operator configured, which is precisely backwards.
    /// </para>
    /// <para>
    /// The test case is the unit of independence, and the two arms run the same ones, so this is a
    /// <b>paired</b> design: per case, take the difference in pass proportion and test whether the
    /// mean difference differs from zero (a paired t-test on n = number of cases). Replays still earn
    /// their keep — they sharpen each case's proportion, reducing the spread of the differences —
    /// but they no longer manufacture sample size.
    /// </para>
    /// <para>
    /// Returns <see langword="null"/> when a p-value is undefined: fewer than two paired cases (a
    /// single case cannot evidence a difference between arms), mismatched arms, or no difference at
    /// all in any case.
    /// </para>
    /// </remarks>
    public static double? PairedTwoSidedPValue(
        IReadOnlyList<double> baselinePerCase,
        IReadOnlyList<double> candidatePerCase)
    {
        if (baselinePerCase.Count != candidatePerCase.Count || baselinePerCase.Count < 2)
            return null;

        int n = baselinePerCase.Count;
        var differences = new double[n];
        for (var i = 0; i < n; i++)
        {
            differences[i] = candidatePerCase[i] - baselinePerCase[i];
        }

        double mean = differences.Average();
        double sumSquares = differences.Sum(d => (d - mean) * (d - mean));
        double standardDeviation = Math.Sqrt(sumSquares / (n - 1));

        if (standardDeviation <= 0)
        {
            // Every case moved by exactly the same amount. If that amount is zero the arms are
            // indistinguishable; otherwise the t statistic is unbounded, so fall back to the
            // two-sided sign test — the probability that all n cases move the same way by chance.
            // That keeps a unanimous change from being discarded, without asserting p = 0 from a
            // handful of cases: two cases give 0.5, ten give ~0.002.
            return mean == 0 ? null : Math.Clamp(2 * Math.Pow(0.5, n), 0, 1);
        }

        double t = mean / (standardDeviation / Math.Sqrt(n));
        return StudentTTwoSidedPValue(Math.Abs(t), degreesOfFreedom: n - 1);
    }

    /// <summary>
    /// Two-sided p-value of Student's t distribution: <c>I_x(df/2, 1/2)</c> with
    /// <c>x = df / (df + t²)</c>.
    /// </summary>
    /// <remarks>
    /// The t distribution, not the normal one, because the paired test above runs on n = the number
    /// of test cases — routinely under 30, where the normal approximation's thinner tails would
    /// under-state the p-value and quietly reintroduce the over-acceptance this change exists to fix.
    /// </remarks>
    private static double StudentTTwoSidedPValue(double t, int degreesOfFreedom)
    {
        if (degreesOfFreedom <= 0)
            return 1;

        double x = degreesOfFreedom / (degreesOfFreedom + t * t);
        return Math.Clamp(RegularizedIncompleteBeta(x, degreesOfFreedom / 2.0, 0.5), 0, 1);
    }

    /// <summary>
    /// Regularized incomplete beta function <c>I_x(a, b)</c>, evaluated with the Lentz continued
    /// fraction and the standard symmetry reflection for fast convergence.
    /// </summary>
    private static double RegularizedIncompleteBeta(double x, double a, double b)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;

        double logPrefix = LogGamma(a + b) - LogGamma(a) - LogGamma(b)
                           + a * Math.Log(x) + b * Math.Log(1 - x);
        double prefix = Math.Exp(logPrefix);

        // The continued fraction converges quickly only on one side of the distribution's mode;
        // reflect onto that side when needed.
        return x < (a + 1) / (a + b + 2)
            ? prefix * BetaContinuedFraction(x, a, b) / a
            : 1 - prefix * BetaContinuedFraction(1 - x, b, a) / b;
    }

    private static double BetaContinuedFraction(double x, double a, double b)
    {
        const int maxIterations = 300;
        const double epsilon = 1e-15;
        const double tiny = 1e-300;

        double qab = a + b;
        double qap = a + 1;
        double qam = a - 1;
        double c = 1;
        double d = 1 - qab * x / qap;
        if (Math.Abs(d) < tiny) d = tiny;
        d = 1 / d;
        double result = d;

        for (var m = 1; m <= maxIterations; m++)
        {
            int m2 = 2 * m;

            // Even step.
            double numerator = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + numerator * d;
            if (Math.Abs(d) < tiny) d = tiny;
            c = 1 + numerator / c;
            if (Math.Abs(c) < tiny) c = tiny;
            d = 1 / d;
            result *= d * c;

            // Odd step.
            numerator = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + numerator * d;
            if (Math.Abs(d) < tiny) d = tiny;
            c = 1 + numerator / c;
            if (Math.Abs(c) < tiny) c = tiny;
            d = 1 / d;
            double delta = d * c;
            result *= delta;

            if (Math.Abs(delta - 1) < epsilon)
                break;
        }

        return result;
    }

    /// <summary>Lanczos approximation of <c>ln Γ(z)</c> (|relative error| &lt; 1e-13).</summary>
    private static double LogGamma(double z)
    {
        double[] coefficients =
        [
            676.5203681218851, -1259.1392167224028, 771.32342877765313,
            -176.61502916214059, 12.507343278686905, -0.13857109526572012,
            9.9843695780195716e-6, 1.5056327351493116e-7,
        ];

        if (z < 0.5)
        {
            // Reflection formula, so the series below only ever runs on the convergent side.
            return Math.Log(Math.PI / Math.Sin(Math.PI * z)) - LogGamma(1 - z);
        }

        z -= 1;
        double x = 0.99999999999980993;
        for (var i = 0; i < coefficients.Length; i++)
        {
            x += coefficients[i] / (z + i + 1);
        }

        double t = z + coefficients.Length - 0.5;
        return 0.5 * Math.Log(2 * Math.PI) + (z + 0.5) * Math.Log(t) - t + Math.Log(x);
    }

    /// <summary>
    /// Cumulative distribution function of the standard normal distribution, via the
    /// Abramowitz &amp; Stegun 7.1.26 error-function approximation (|error| &lt; 1.5e-7).
    /// </summary>
    private static double StandardNormalCdf(double x)
        => 0.5 * (1 + Erf(x / Math.Sqrt(2)));

    private static double Erf(double x)
    {
        int sign = Math.Sign(x);
        x = Math.Abs(x);

        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;

        double t = 1.0 / (1.0 + p * x);
        double y = 1.0 - ((((a5 * t + a4) * t + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);

        return sign * y;
    }
}
