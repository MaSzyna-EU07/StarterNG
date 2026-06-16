using System.Reflection;

namespace StarterNG;

/// <summary>Static, display-ready information about the running launcher build.</summary>
public static class AppInfo
{
    /// <summary>
    /// The launcher's assembly version (e.g. "1.2.3.4"), with a " (DEBUG)" suffix
    /// appended for Debug builds so debug and release binaries are easy to tell
    /// apart. Computed once; safe to bind with {x:Static}.
    /// </summary>
    public static string Version { get; } = BuildVersion();

    private static string BuildVersion()
    {
        string text = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
#if DEBUG
        text += " (DEBUG)";
#endif
        return text;
    }
}
