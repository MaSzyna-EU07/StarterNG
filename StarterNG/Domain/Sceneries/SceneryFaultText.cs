using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StarterNG.Domain.Sceneries;

/// <summary>A scenery's faults as they are read: where each one is, in a column, and what it is.</summary>
public static class SceneryFaultText
{
    /// <param name="limit">Most it lists; the rest are only counted.</param>
    /// <param name="text">The wording for a key, in the language shown.</param>
    public static string Format(IReadOnlyList<SceneryFault> faults, int limit, Func<string, string> text)
    {
        if (faults.Count == 0)
            return "";

        var shown = faults.Take(limit).Select(f => (Where: $"{f.File}:{f.Line}", Fault: f)).ToList();
        int width = shown.Max(f => f.Where.Length) + 2;
        var lines = new StringBuilder();
        foreach (var (where, fault) in shown)
        {
            lines.Append(where.PadRight(width)).Append(Describe(fault, text)).Append('\n');
            // What it costs, under it: too long to follow on the same line.
            if (fault.Lost is { } lost)
                lines.Append(' ', width).Append(string.Format(text("SyntaxLost"), lost)).Append('\n');
            if (fault.Unmade is { } unmade)
                lines.Append(' ', width).Append(string.Format(text("SyntaxUnmade"), unmade)).Append('\n');
        }
        if (faults.Count > limit)
            lines.Append(string.Format(text("SyntaxMore"), faults.Count - limit));
        return lines.ToString().TrimEnd();
    }

    private static string Describe(SceneryFault fault, Func<string, string> text) =>
        string.Format(text("Syntax" + fault.Kind), fault.Subject ?? "?", fault.Token ?? "");
}
