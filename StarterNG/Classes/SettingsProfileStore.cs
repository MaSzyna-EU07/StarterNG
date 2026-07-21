using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StarterNG.Classes;

public static class SettingsProfileStore
{
    public static string DirectoryPath { get; } =
        Path.Combine(Settings.UserConfigDirectory(), "starter", "profiles");

    public static IReadOnlyList<string> ListProfiles()
    {
        try
        {
            if (!Directory.Exists(DirectoryPath)) return Array.Empty<string>();
            return Directory.GetFiles(DirectoryPath, "*.ini")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList()!;
        }
        catch { return Array.Empty<string>(); }
    }

    public static string PathFor(string name)
    {
        string safe = name.Trim();
        foreach (char c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '_');
        return Path.Combine(DirectoryPath, safe + ".ini");
    }
}
