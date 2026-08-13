using Nordstein.Core.Common.Time;

namespace Proxytrace.Licensing.Tests;

/// <summary>
/// A test clock whose time can be advanced deterministically.
/// </summary>
internal sealed class MutableClock : IClock
{
    public MutableClock(DateTimeOffset start) => UtcNow = start;

    public DateTimeOffset UtcNow { get; set; }

    public void Advance(TimeSpan delta) => UtcNow += delta;
}
