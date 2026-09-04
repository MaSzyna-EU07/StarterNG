using System;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure.Adapters;

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;

    public DateTime UtcNow => DateTime.UtcNow;
}
