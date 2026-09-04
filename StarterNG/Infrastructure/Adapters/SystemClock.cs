using System;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure.Adapters;

/// <summary><see cref="IClock"/> backed by the machine clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;

    public DateTime UtcNow => DateTime.UtcNow;
}
