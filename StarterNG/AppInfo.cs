using System.Reflection;

namespace StarterNG;

public static class AppInfo
{

    public static string Version { get; } = BuildVersion();

    private static string BuildVersion()
    {
        string text = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
#if DEBUG
        text += " (DEBUG)";
#endif
        return text + " public beta";
    }
}
