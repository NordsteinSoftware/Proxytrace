namespace Proxytrace.Licensing.Exceptions;

/// <summary>
/// Why a license JWT was rejected.
/// </summary>
public enum InvalidLicenseReason
{
    Malformed,
    BadSignature,
    WrongIssuer,
    WrongAudience,
    Expired,
    MissingClaim,
}

/// <summary>
/// Thrown when a configured license JWT cannot be validated. Propagating out of container
/// build causes the host to fail fast rather than silently downgrading to Free.
/// </summary>
public sealed class InvalidLicenseException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidLicenseException"/> class.
    /// </summary>
    public InvalidLicenseException(InvalidLicenseReason reason)
        : this(reason, $"The configured license is invalid: {reason}.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidLicenseException"/> class.
    /// </summary>
    public InvalidLicenseException(InvalidLicenseReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidLicenseException"/> class.
    /// </summary>
    public InvalidLicenseException(InvalidLicenseReason reason, string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = reason;
    }

    /// <summary>
    /// The specific reason validation failed.
    /// </summary>
    public InvalidLicenseReason Reason { get; }
}
