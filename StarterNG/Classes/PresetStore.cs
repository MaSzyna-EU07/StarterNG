using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace StarterNG.Classes;

public enum PresetSort
{
    Name,
    Track
}

public sealed class TrainsetPreset
{
    public string Name { get; set; } = "";

    public string Entry { get; set; } = "";
}

public sealed class PresetCollection
{
    public List<TrainsetPreset> Presets { get; set; } = new();
}

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

    public static PresetSort SortMode { get; set; } = PresetSort.Name;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        TypeInfoResolver = PresetJsonContext.Default,
    };

    private static JsonTypeInfo<PresetCollection> TypeInfo =>
        (JsonTypeInfo<PresetCollection>)Options.GetTypeInfo(typeof(PresetCollection));

    public static IReadOnlyList<TrainsetPreset> All()
    {
        var list = Load().Presets.ToList();
        if (SortMode == PresetSort.Track)
            list.Sort((a, b) => string.Compare(TrackOf(a), TrackOf(b), StringComparison.OrdinalIgnoreCase));
        else
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

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

    public static void Delete(string name)
    {
        var col = Load();
        if (col.Presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) > 0)
            Persist(col);
    }

    public static int ImportMagazyn(string? path = null)
    {
        path ??= FindMagazynPath();
        if (path is null || !File.Exists(path)) return 0;

        int added = 0;
        try
        {
            string text = File.ReadAllText(path, Encoding.GetEncoding(1250));
            var section = Regex.Matches(text,
                @"\[TRAINSET\d+\s*=\s*([^\]]*)\](.*?)(?=\[TRAINSET|\z)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match m in section)
            {
                string name = m.Groups[1].Value.Trim();
                if (name.Length == 0) name = $"import_{added + 1}";

                var nodes = new List<string>();
                foreach (Match line in Regex.Matches(m.Groups[2].Value, @"(?m)^\s*\d+\s*=\s*(.+)$"))
                {
                    string body = line.Groups[1].Value.Trim();
                    if (!body.Contains("enddynamic", StringComparison.OrdinalIgnoreCase))
                        body += " enddynamic";
                    nodes.Add(body);
                }
                if (nodes.Count == 0) continue;

                var sb = new StringBuilder();
                sb.Append("trainset ").Append(Sanitize(name)).Append(" none 0 0\n");
                foreach (string n in nodes)
                    sb.Append(n).Append('\n');
                sb.Append("endtrainset\n");

                Save(name, sb.ToString());
                added++;
            }
        }
        catch (Exception ex)
        {
            Infrastructure.Diagnostics.Log($"Import {path}", ex);
        }
        return added;
    }

    private static string? FindMagazynPath()
    {
        string cwd = Directory.GetCurrentDirectory();
        foreach (string rel in new[]
                 {
                     Path.Combine("starter", "magazyn.ini"),
                     "starter.ini",
                     "RAINSTED.INI"
                 })
        {
            string p = Path.Combine(cwd, rel);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private static string TrackOf(TrainsetPreset p)
    {
        var m = Regex.Match(p.Entry ?? "", @"^\s*trainset\s+\S+\s+(\S+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : "";
    }

    private static string Sanitize(string name) =>
        string.IsNullOrWhiteSpace(name) ? "preset" : name.Replace(' ', '_');

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

        }
    }
}

public static class ConsistText
{
    public static string Serialize(Trainset trainset) => trainset.ToSceneryEntry();

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

[JsonSerializable(typeof(PresetCollection))]
[JsonSerializable(typeof(TrainsetPreset))]
internal partial class PresetJsonContext : JsonSerializerContext
{
}
