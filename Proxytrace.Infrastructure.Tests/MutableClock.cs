using Proxytrace.Common.Time;

namespace Proxytrace.Infrastructure.Tests;

/// <summary>
/// A test clock whose time can be advanced deterministically, so expiry-based behaviour can be
/// exercised without sleeping. Each test constructs its own instance — never share one.
/// </summary>
internal sealed class MutableClock : IClock
{
    public MutableClock()
        : this(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
    {
    }

    public MutableClock(DateTimeOffset start) => UtcNow = start;

    public DateTimeOffset UtcNow { get; set; }

    public void Advance(TimeSpan delta) => UtcNow += delta;
}
