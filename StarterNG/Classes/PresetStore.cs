using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using StarterNG.Application;
using StarterNG.Application.Abstractions;
using StarterNG.Infrastructure.Adapters;

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

    /// <summary>The old starter's consists taken in at a start, by <see cref="PresetFile.Fingerprint"/>.</summary>
    public List<string> OldStarterSeen { get; set; } = new();
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

    private static PresetFile? _disk;

    private static PresetFile Disk =>
        _disk ??= new PresetFile(AppServices.Current.Files, AppServices.Current.Log, FilePath);

    public static IReadOnlyList<TrainsetPreset> All()
    {
        var list = Disk.Presets();
        if (SortMode == PresetSort.Track)
            list.Sort((a, b) => string.Compare(TrackOf(a), TrackOf(b), StringComparison.OrdinalIgnoreCase));
        else
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public static void Save(string? name, string? entry) => Disk.Save(name, entry);

    public static void Delete(string name) => Disk.Delete(name);

    public static int ImportMagazyn(string? path = null)
    {
        path ??= FindMagazynPath();
        if (path is null || !File.Exists(path)) return 0;

        int added = 0;
        try
        {
            foreach (var consist in OldStarterWarehouse.Read(LegacyText.Decode(File.ReadAllBytes(path))))
            {
                Save(consist.Name, consist.Entry);
                added++;
            }
        }
        catch (Exception ex)
        {
            Infrastructure.Diagnostics.Log($"Import {path}", ex);
        }
        return added;
    }

    /// <summary>
    /// At the start: what the old starter put in its warehouse since the last start
    /// joins this one, as that starter showed its magazyn.ini each time it was opened.
    /// </summary>
    public static void TakeFromOldStarter()
    {
        string? path = FindMagazynPath();
        if (path is null)
            return;

        try
        {
            Disk.TakeFromOldStarter(OldStarterWarehouse.Read(LegacyText.Decode(File.ReadAllBytes(path))));
        }
        catch (Exception ex)
        {
            Infrastructure.Diagnostics.Log($"Import {path}", ex);
        }
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
}

/// <summary>
/// The old starter's magazyn.ini: a [TRAINSETn=name] line for each consist, then
/// its vehicles as numbered "node … enddynamic" lines.
/// </summary>
public static class OldStarterWarehouse
{
    public static List<TrainsetPreset> Read(string text)
    {
        var consists = new List<TrainsetPreset>();
        var sections = Regex.Matches(text,
            @"\[TRAINSET\d+\s*=\s*([^\]]*)\](.*?)(?=\[TRAINSET|\z)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match m in sections)
        {
            string name = m.Groups[1].Value.Trim();
            if (name.Length == 0) name = $"import_{consists.Count + 1}";

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

            consists.Add(new TrainsetPreset { Name = name, Entry = sb.ToString() });
        }
        return consists;
    }

    private static string Sanitize(string name) =>
        string.IsNullOrWhiteSpace(name) ? "preset" : name.Replace(' ', '_');
}

/// <summary>The presets as one file holds them; <see cref="PresetStore"/> is the player's own.</summary>
public sealed class PresetFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        TypeInfoResolver = PresetJsonContext.Default,
    };

    private static JsonTypeInfo<PresetCollection> TypeInfo =>
        (JsonTypeInfo<PresetCollection>)Options.GetTypeInfo(typeof(PresetCollection));

    private readonly IFileSystem _files;
    private readonly IDiagnosticsLog _log;
    private readonly string _path;

    public PresetFile(IFileSystem files, IDiagnosticsLog log, string path)
    {
        _files = files;
        _log = log;
        _path = path;
    }

    public List<TrainsetPreset> Presets()
    {
        TryRead(out var col);
        return col.Presets;
    }

    public void Save(string? name, string? entry)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0 || string.IsNullOrWhiteSpace(entry))
            return;

        bool read = TryRead(out var col);
        col.Presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        col.Presets.Add(new TrainsetPreset { Name = name, Entry = entry! });
        col.Presets.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        Persist(col, read);
    }

    public void Delete(string name)
    {
        bool read = TryRead(out var col);
        if (col.Presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) > 0)
            Persist(col, read);
    }

    /// <summary>Where a consist of the old starter goes when one of its name with other vehicles is here.</summary>
    public const string OldStarterSuffix = " (magazyn.ini)";

    /// <summary>
    /// Takes in the old starter's consists not taken at an earlier start, so one
    /// removed here stays removed while the old starter keeps it as it was. One
    /// whose name is here with other vehicles comes in beside it, unless what is
    /// here is that consist as an earlier start brought it, untouched since.
    /// </summary>
    /// <returns>How many came in.</returns>
    public int TakeFromOldStarter(IReadOnlyList<TrainsetPreset> found)
    {
        // A file that cannot be read is not replaced by an import nobody asked for.
        if (found.Count == 0 || !TryRead(out var col))
            return 0;

        var seenBefore = new HashSet<string>(col.OldStarterSeen, StringComparer.Ordinal);
        var seen = new HashSet<string>(seenBefore, StringComparer.Ordinal);
        int added = 0;
        foreach (var consist in found)
        {
            if (!seen.Add(Fingerprint(consist)))
                continue;

            var here = col.Presets.Find(p => string.Equals(p.Name, consist.Name, StringComparison.OrdinalIgnoreCase));
            if (here is not null && here.Entry == consist.Entry)
                continue;

            string name = here is null || seenBefore.Contains(Fingerprint(here))
                ? consist.Name
                : consist.Name + OldStarterSuffix;
            col.Presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            col.Presets.Add(new TrainsetPreset { Name = name, Entry = consist.Entry });
            added++;
        }

        if (seen.Count == seenBefore.Count)
            return 0;

        col.OldStarterSeen = seen.Order(StringComparer.Ordinal).ToList();
        col.Presets.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        Persist(col, read: true);
        return added;
    }

    /// <summary>A consist by its name and vehicles, short enough to keep each one ever taken in.</summary>
    public static string Fingerprint(TrainsetPreset preset) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(preset.Name + "\n" + preset.Entry)));

    /// <summary>The file's presets; false, with none, when there is a file but they cannot be had from it.</summary>
    private bool TryRead(out PresetCollection col)
    {
        col = new PresetCollection();
        try
        {
            if (!_files.FileExists(_path))
                return true;

            var read = JsonSerializer.Deserialize(_files.ReadAllText(_path), TypeInfo);
            if (read?.Presets != null)
            {
                col = read;
                return true;
            }
            _log.Log($"reading {_path}: no presets in it");
        }
        catch (Exception ex)
        {
            _log.Log($"reading {_path}", ex);
        }
        return false;
    }

    // What was not read is not in the list: written over, those presets are gone, so
    // a copy goes first and without one the file stays - the new preset waits.
    private void Persist(PresetCollection col, bool read)
    {
        try
        {
            _files.CreateDirectory(Path.GetDirectoryName(_path)!);
            if (!read)
                _files.KeepAside(_path, _log);
            _files.WriteAllTextAtomic(_path, JsonSerializer.Serialize(col, TypeInfo));
        }
        catch (Exception ex)
        {
            _log.Log($"saving {_path}", ex);
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
