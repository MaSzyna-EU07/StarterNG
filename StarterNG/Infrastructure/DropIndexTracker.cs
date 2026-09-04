using System;
using System.Collections.Generic;

namespace StarterNG.Infrastructure;

public sealed class DropIndexTracker
{
    private const double Hysteresis = 14;
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(60);

    private readonly List<double> _midpoints = new();
    private DateTime _changedAt = DateTime.MinValue;

    public int Index { get; private set; } = -1;

    public void Capture(IEnumerable<double> midpoints)
    {
        _midpoints.Clear();
        _midpoints.AddRange(midpoints);
        Index = -1;
        _changedAt = DateTime.MinValue;
    }

    public void Reset()
    {
        _midpoints.Clear();
        Index = -1;
    }

    public bool Update(double x)
    {
        int candidate = Index;

        if (candidate < 0)
        {
            candidate = 0;
            while (candidate < _midpoints.Count && x > _midpoints[candidate])
                candidate++;
        }
        else
        {
            while (candidate < _midpoints.Count && x > _midpoints[candidate] + Hysteresis)
                candidate++;
            while (candidate > 0 && x < _midpoints[candidate - 1] - Hysteresis)
                candidate--;
        }

        if (candidate == Index)
            return false;

        var now = DateTime.UtcNow;
        if (Index >= 0 && now - _changedAt < Quiet)
            return false;

        _changedAt = now;
        Index = candidate;
        return true;
    }
}
