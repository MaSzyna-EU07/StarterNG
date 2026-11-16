using System;
using Avalonia;

namespace StarterNG.Views.Tapes;

/// <summary>
/// The line of a tape, one column of pixels at a time: of each column, the first, the lowest, the highest and the
/// last point in it, entered where the column started and left where it ended, so a long trip draws as fast as a
/// short one. The figures go out through the three calls given: one begun at its first point, a line to each next,
/// and its end.
/// </summary>
/// <remarks>
/// Its state is kept in fields, not in the locals of a local function, which Sonar's analysis doesn't see changed
/// by it (S2583 took the end of a figure after a gap for code never run).
/// </remarks>
internal sealed class ColumnLine(Action<Point> begin, Action<Point> lineTo, Action end)
{
    private bool _begun;
    private int _column = int.MinValue;
    private double _top, _bottom, _first, _last;

    /// <summary>The point of a sample, at its column of pixels.</summary>
    public void Add(int column, double value)
    {
        if (column != _column)
        {
            Flush();
            _column = column;
            _top = _bottom = _first = value;
        }
        _top = Math.Min(_top, value);
        _bottom = Math.Max(_bottom, value);
        _last = value;
    }

    /// <summary>The line ends here, at a gap or at the end of the tape; a point added after begins a figure of its own.</summary>
    public void Break()
    {
        Flush();
        _column = int.MinValue;
        if (!_begun)
            return;
        end();
        _begun = false;
    }

    private void Flush()
    {
        if (_column == int.MinValue)
            return;
        // Enter where the column started, leave where it ended.
        var (a, b) = _first <= _last ? (_top, _bottom) : (_bottom, _top);
        foreach (double v in new[] { _first, a, b, _last })
        {
            var p = new Point(_column, v);
            if (_begun)
                lineTo(p);
            else
            {
                begin(p);
                _begun = true;
            }
        }
    }
}
