namespace Proxytrace.Licensing.Exceptions;

/// <summary>
/// Thrown when an operation would exceed a numeric limit enforced by the current license tier.
/// Surfaced to clients as HTTP 402 Payment Required.
/// </summary>
public sealed class LicenseLimitExceededException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LicenseLimitExceededException"/> class.
    /// </summary>
    public LicenseLimitExceededException(LicenseLimit limit, long current, long max)
        : base($"The limit '{limit}' has been reached ({current}/{max}).")
    {
        Limit = limit;
        Current = current;
        Max = max;
    }

    /// <summary>
    /// The limit that was exceeded.
    /// </summary>
    public LicenseLimit Limit { get; }

    /// <summary>
    /// The current usage value.
    /// </summary>
    public long Current { get; }

    /// <summary>
    /// The maximum allowed value.
    /// </summary>
    public long Max { get; }
}
