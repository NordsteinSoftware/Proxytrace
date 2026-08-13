namespace Nordstein.Core.Common.Time.Internal;

/// <summary>
/// Default <see cref="IClock"/> backed by the real system clock.
/// </summary>
internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
