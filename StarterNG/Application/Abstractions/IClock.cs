namespace StarterNG.Application.Abstractions;

using System;

/// <summary>
/// Port over the system clock. Scenery weather defaults, timetable rendering and
/// log timestamps all depend on "now"; routing them through a clock keeps those
/// rules deterministic under test.
/// </summary>
public interface IClock
{
    DateTime Now { get; }

    DateTime UtcNow { get; }
}
