namespace StarterNG.Application.Abstractions;

using System;

public interface IClock
{
    DateTime Now { get; }

    DateTime UtcNow { get; }
}
