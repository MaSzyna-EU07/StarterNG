namespace StarterNG.Domain;

/// <summary>
/// File names as the simulator makes them: its parser folds every word it reads
/// to lower case - ASCII letters only - the files a scenery includes and the
/// scenario given with <c>-s</c> among them, and opens the file by that name.
/// Where letter case counts, as on Linux, a file is found only when it is named
/// so on disk.
/// </summary>
public static class SimulatorNames
{
    public static string Lower(string name) =>
        string.Create(name.Length, name, static (folded, source) =>
        {
            for (int i = 0; i < source.Length; i++)
                folded[i] = source[i] is >= 'A' and <= 'Z' ? (char)(source[i] + ('a' - 'A')) : source[i];
        });
}
