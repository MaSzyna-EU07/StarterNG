using System;
using System.Collections.Generic;
using System.Linq;

namespace StarterNG.Views;

/// <summary>
/// The simulators offered in the settings after the folder was looked at again: a build
/// copied in while the starter runs joins the list, and what was chosen stays chosen, even
/// a file picked from elsewhere or one since removed - looking again never changes the choice.
/// </summary>
public static class ExecutableList
{
    /// <summary>The entries that follow "automatic", and the place of <paramref name="chosen"/> among them.</summary>
    /// <param name="found">The simulators in the folder now, newest first.</param>
    /// <param name="chosen">The entry chosen, or null for "automatic" or nothing chosen yet.</param>
    /// <returns>Entries, and the index of the chosen one in them, -1 when <paramref name="chosen"/> is null.</returns>
    public static (IReadOnlyList<string> Entries, int Chosen) Refreshed(IReadOnlyList<string> found, string? chosen)
    {
        var entries = found.ToList();
        if (chosen is null)
            return (entries, -1);

        int at = entries.FindIndex(entry => string.Equals(entry, chosen, StringComparison.OrdinalIgnoreCase));
        if (at >= 0)
            return (entries, at);

        entries.Add(chosen);
        return (entries, entries.Count - 1);
    }
}
