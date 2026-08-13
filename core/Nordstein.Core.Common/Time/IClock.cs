namespace Nordstein.Core.Common.Time;

/// <summary>
/// Abstraction over the system clock so time-dependent logic can be tested deterministically.
/// </summary>
public interface IClock
{
    /// <summary>
    /// The current UTC time.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}
