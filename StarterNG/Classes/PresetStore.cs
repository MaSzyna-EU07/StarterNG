using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace StarterNG.Classes;

/// <summary>One saved consist in the user's vehicle warehouse.</summary>
public sealed class TrainsetPreset
{
    /// <summary>Display name shown in the "load from warehouse" menu.</summary>
    public string Name { get; set; } = "";

    /// <summary>Complete "trainset … endtrainset" scenery entry (self-contained).</summary>
    public string Entry { get; set; } = "";
}

/// <summary>Root object persisted to userpresets.json.</summary>
public sealed class PresetCollection
{
    public List<TrainsetPreset> Presets { get; set; } = new();
}

/// <summary>
/// The user's vehicle/consist "warehouse": named trainset presets persisted as JSON.
/// Stored under the per-user config directory so it survives reinstalls:
///   Windows: %APPDATA%\MaSzyna\starter\userpresets.json
///   macOS:   ~/Library/Application Support/MaSzyna/starter/userpresets.json
///   Linux:   ~/.config/MaSzyna/starter/userpresets.json
/// All operations are best-effort and never throw.
/// </summary>
public static class PresetStore
{
    private static string ResolvePath()
    {
        if (OperatingSystem.IsWindows())
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "MaSzyna", "starter", "userpresets.json");
        }
        if (OperatingSystem.IsMacOS())
        {
            string home = Environment.GetEnvironmentVariable("HOME") ?? AppContext.BaseDirectory;
            return Path.Combine(home, "Library", "Application Support", "MaSzyna", "starter", "userpresets.json");
        }
        {
            string home = Environment.GetEnvironmentVariable("HOME") ?? AppContext.BaseDirectory;
            return Path.Combine(home, ".config", "MaSzyna", "starter", "userpresets.json");
        }
    }

    public static string FilePath { get; } = ResolvePath();

    // Source-generated metadata keeps (de)serialisation AOT/trim-safe, matching the
    // approach used for the vehicle database.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        TypeInfoResolver = PresetJsonContext.Default,
    };

    private static JsonTypeInfo<PresetCollection> TypeInfo =>
        (JsonTypeInfo<PresetCollection>)Options.GetTypeInfo(typeof(PresetCollection));

    /// <summary>All saved presets, sorted by name. Empty when none / unreadable.</summary>
    public static IReadOnlyList<TrainsetPreset> All() => Load().Presets;

    /// <summary>
    /// Saves (or overwrites, by name) a consist preset. No-op on a blank name or
    /// empty entry.
    /// </summary>
    public static void Save(string? name, string? entry)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0 || string.IsNullOrWhiteSpace(entry))
            return;

        var col = Load();
        col.Presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        col.Presets.Add(new TrainsetPreset { Name = name, Entry = entry! });
        col.Presets.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        Persist(col);
    }

    /// <summary>Removes a preset by name (case-insensitive).</summary>
    public static void Delete(string name)
    {
        var col = Load();
        if (col.Presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) > 0)
            Persist(col);
    }

    private static PresetCollection Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var col = JsonSerializer.Deserialize(File.ReadAllText(FilePath), TypeInfo);
                if (col?.Presets != null)
                    return col;
            }
        }
        catch
        {
            // unreadable / malformed - start from an empty warehouse
        }
        return new PresetCollection();
    }

    private static void Persist(PresetCollection col)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(col, TypeInfo));
        }
        catch
        {
            // best-effort: a failed save just means the warehouse isn't updated
        }
    }
}

/// <summary>
/// Helpers to convert a consist to/from its canonical scenery text - the full
/// "trainset … endtrainset" block, exactly what is placed on the clipboard.
/// </summary>
public static class ConsistText
{
    /// <summary>The complete scenery entry for a trainset (incl. endtrainset).</summary>
    public static string Serialize(Trainset trainset) => trainset.ToSceneryEntry();

    /// <summary>
    /// Parses the vehicles out of a complete trainset entry. Returns null when the
    /// text holds no usable node::dynamic vehicles.
    /// </summary>
    public static List<Dynamic>? VehiclesFrom(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            var trainset = new Trainset(text);
            return trainset.Vehicles is { Count: > 0 } ? trainset.Vehicles : null;
        }
        catch
        {
            return null;
        }
    }
}

// Source-generated JSON metadata (AOT/trim-safe; no runtime reflection).
[JsonSerializable(typeof(PresetCollection))]
[JsonSerializable(typeof(TrainsetPreset))]
internal partial class PresetJsonContext : JsonSerializerContext
{
}
