using System;
using System.Collections.Generic;

namespace StarterNG.Domain.Sceneries;

/// <summary>
/// Which file and line a place in a scenery's text is - its own file, or one put
/// in place of its include by <see cref="SceneryIncludes"/>, counted from that
/// file's own first line.
/// </summary>
public sealed class SceneryLines
{
    private readonly List<int> _starts = new();
    private readonly List<(string File, int Line)> _lines = new();

    public SceneryLines(string text, string file)
    {
        var outer = new Stack<(string File, int Line)>();
        string current = file;
        int number = 1;

        for (int start = 0; ;)
        {
            int end = text.IndexOf('\n', start);
            if (end < 0)
                end = text.Length;
            var line = text.AsSpan(start, end - start);

            _starts.Add(start);
            if (line.StartsWith(SceneryIncludes.BeginMarker, StringComparison.Ordinal))
            {
                // The marker stands where the include was; the file's lines follow.
                _lines.Add((current, number));
                outer.Push((current, number));
                current = line[SceneryIncludes.BeginMarker.Length..].Trim().ToString();
                number = 1;
            }
            else if (line.StartsWith(SceneryIncludes.EndMarker, StringComparison.Ordinal) && outer.Count > 0)
            {
                (current, number) = outer.Pop();
                _lines.Add((current, number++));
            }
            else
            {
                _lines.Add((current, number++));
            }

            if (end == text.Length)
                break;
            start = end + 1;
        }
    }

    public (string File, int Line) At(int index)
    {
        int found = _starts.BinarySearch(index);
        return _lines[Math.Max(0, found >= 0 ? found : ~found - 1)];
    }

    /// <summary>A mistake at a place in the text, told by its file and line.</summary>
    public SceneryFault Fault(SceneryFaultKind kind, int at, string? subject = null, string? token = null)
    {
        var (file, line) = At(at);
        return new SceneryFault(kind, file, line, subject, token) { Offset = at };
    }
}
