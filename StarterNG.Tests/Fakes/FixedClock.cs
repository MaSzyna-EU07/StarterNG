using StarterNG.Application.Abstractions;

namespace StarterNG.Tests.Fakes;

/// <summary>A clock frozen at a chosen instant.</summary>
public sealed class FixedClock : IClock
{
    public FixedClock(DateTime now)
    {
        Now = now;
    }

    public DateTime Now { get; set; }

    public DateTime UtcNow => Now.ToUniversalTime();
}
