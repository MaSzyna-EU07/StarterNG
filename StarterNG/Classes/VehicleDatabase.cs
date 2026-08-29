using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace StarterNG.Classes;

public class VehicleEntry
{
    [JsonPropertyName("uuid")] public string? Uuid { get; set; }
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("groups")] public List<VehicleGroup> Groups { get; set; } = new();
    [JsonPropertyName("textures")] public List<VehicleTexture> Textures { get; set; } = new();
    [JsonPropertyName("sets")] public List<VehicleSet> Sets { get; set; } = new();
    [JsonPropertyName("unknown")] public List<string> Unknown { get; set; } = new();
}

public class VehicleEntryCollection
{
    [JsonPropertyName("vehicles")] public List<VehicleEntry> Vehicles { get; set; } = new();
}

public class VehicleGroup
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("mini")] public string? Mini { get; set; }
    [JsonPropertyName("archived")] public bool Archived { get; set; }
    [JsonPropertyName("implicit")] public bool Implicit { get; set; }
}

public class VehicleTexture
{
    [JsonPropertyName("uuid")] public string? Uuid { get; set; }
    [JsonPropertyName("directory")] public string Directory { get; set; } = "";
    [JsonPropertyName("skinfile")] public string Skinfile { get; set; } = "";
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("group")] public string? Group { get; set; }
    [JsonPropertyName("mini_ref")] public string? MiniRef { get; set; }
    [JsonPropertyName("texture_mini")] public string? TextureMini { get; set; }
    [JsonPropertyName("wreck")] public bool Wreck { get; set; }
    [JsonPropertyName("aliases")] public List<VehicleAlias> Aliases { get; set; } = new();
    [JsonPropertyName("meta")] public VehicleMeta? Meta { get; set; }

    [JsonIgnore] public string FullPath => Directory + Skinfile;

    [JsonIgnore] public string ResolvedClass { get; internal set; } = "";

    [JsonIgnore] public string? ResolvedCategory { get; internal set; }

    [JsonIgnore] public bool ResolvedArchived { get; internal set; }
}

public class VehicleAlias
{
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("group")] public string? Group { get; set; }
    [JsonPropertyName("mini_ref")] public string? MiniRef { get; set; }
    [JsonPropertyName("texture_mini")] public string? TextureMini { get; set; }
}

public class VehicleMeta
{
    [JsonPropertyName("raw")] public string? Raw { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("vehicle")] public string? Vehicle { get; set; }
    [JsonPropertyName("operator")] public string? Operator { get; set; }
    [JsonPropertyName("depot")] public string? Depot { get; set; }
    [JsonPropertyName("revision_date")] public string? RevisionDate { get; set; }
    [JsonPropertyName("revision_place")] public string? RevisionPlace { get; set; }
    [JsonPropertyName("texture_author")] public string? TextureAuthor { get; set; }
    [JsonPropertyName("photo_author")] public string? PhotoAuthor { get; set; }
    [JsonPropertyName("extra")] public List<string> Extra { get; set; } = new();
}

public class VehicleDatabase
{
    public List<VehicleTexture> Textures { get; } = new();
    public Dictionary<string, VehicleGroup> GroupsById { get; } = new();
    public List<VehicleSet> Sets { get; } = new();

    public Dictionary<string, VehicleSet> SetByTextureUuid { get; } = new();

    public Dictionary<string, VehicleTexture> TextureByUuid { get; } = new();

    public Dictionary<string, VehicleTexture> TextureBySkin { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        TypeInfoResolver = VehicleJsonContext.Default,
    };

    public void Load(string directory = "databases/vehicles/")
    {
        BeginLoad();
        foreach (string file in EnumerateFiles(directory))
            LoadFile(file);
        EndLoad();
    }

    public void BeginLoad()
    {
        Textures.Clear();
        GroupsById.Clear();
        Sets.Clear();
        SetByTextureUuid.Clear();
        TextureByUuid.Clear();
        TextureBySkin.Clear();
    }

    public void EndLoad()
    {
        BuildSetIndex();
        BuildTextureIndex();
    }

    public static List<string> EnumerateFiles(string directory = "databases/vehicles/")
    {
        if (!Directory.Exists(directory))
            return new List<string>();

        string merged = Path.Combine(directory, "vehicles.json");
        if (File.Exists(merged))
            return new List<string> { merged };

        return Directory.GetFiles(directory, "*.json").ToList();
    }

    public void LoadFile(string file)
    {
        if (string.Equals(Path.GetFileName(file), "vehicles.json", StringComparison.OrdinalIgnoreCase))
            LoadMerged(file);
        else
            LoadEntryFile(file);
    }

    private void LoadMerged(string file)
    {
        try
        {
            var collection = JsonSerializer.Deserialize(
                File.ReadAllText(file),
                (JsonTypeInfo<VehicleEntryCollection>)JsonOpts.GetTypeInfo(typeof(VehicleEntryCollection)));
            if (collection?.Vehicles is null) return;
            foreach (var entry in collection.Vehicles)
                Ingest(entry);
        }
        catch
        {

        }
    }

    private void LoadEntryFile(string file)
    {
        try
        {
            var entry = JsonSerializer.Deserialize(
                File.ReadAllText(file),
                (JsonTypeInfo<VehicleEntry>)JsonOpts.GetTypeInfo(typeof(VehicleEntry)));
            if (entry is not null)
                Ingest(entry);
        }
        catch
        {

        }
    }

    private void Ingest(VehicleEntry entry)
    {
        foreach (var group in entry.Groups)
        {
            if (!string.IsNullOrEmpty(group.Id))
                GroupsById.TryAdd(group.Id, group);
        }

        foreach (var texture in entry.Textures)
        {

            if (!string.IsNullOrEmpty(texture.Uuid))
                TextureByUuid[texture.Uuid!] = texture;
            if (!string.IsNullOrEmpty(texture.Skinfile))
                TextureBySkin.TryAdd(Path.GetFileNameWithoutExtension(texture.Skinfile).ToLowerInvariant(), texture);

            if (texture.Wreck) continue;
            Textures.Add(texture);
        }

        Sets.AddRange(entry.Sets);
    }

    public List<VehicleTexture>? ResolveSet(VehicleTexture texture)
    {
        if (string.IsNullOrEmpty(texture.Uuid)) return null;
        if (!SetByTextureUuid.TryGetValue(texture.Uuid!, out var set) || set.TextureRefs is null)
            return null;

        var cars = new List<VehicleTexture>();
        foreach (string uuid in set.TextureRefs)
        {
            if (!string.IsNullOrEmpty(uuid) && TextureByUuid.TryGetValue(uuid, out var tex))
                cars.Add(tex);
        }
        return cars.Count > 0 ? cars : null;
    }

    private void BuildSetIndex()
    {
        foreach (var set in Sets)
        {
            if (set.TextureRefs is null) continue;
            foreach (string uuid in set.TextureRefs)
            {
                if (!string.IsNullOrEmpty(uuid))
                    SetByTextureUuid[uuid] = set;
            }
        }
    }

    private void BuildTextureIndex()
    {
        foreach (var t in Textures)
        {
            VehicleGroup? g = null;
            if (t.Group != null)
                GroupsById.TryGetValue(t.Group, out g);

            string mini = g != null && !string.IsNullOrEmpty(g.Mini)
                ? g.Mini!
                : t.MiniRef ?? "";

            string? category = g?.Category;

            if (category == "*")
            {
                string source = !string.IsNullOrEmpty(mini) ? mini : (t.TextureMini ?? "");
                if (source.Length > 0)
                    category = char.ToUpperInvariant(source[0]).ToString();
            }

            t.ResolvedCategory = category;
            t.ResolvedClass = mini;
            t.ResolvedArchived = g?.Archived ?? false;
        }
    }

    public bool IsSetFollower(VehicleTexture texture)
    {
        if (string.IsNullOrEmpty(texture.Uuid)) return false;
        if (!SetByTextureUuid.TryGetValue(texture.Uuid!, out var set) ||
            set.TextureRefs is null || set.TextureRefs.Count < 2)
            return false;

        string? lead = set.TextureRefs.FirstOrDefault(r => !string.IsNullOrEmpty(r));
        return lead != null &&
               !string.Equals(lead, texture.Uuid, StringComparison.OrdinalIgnoreCase);
    }

    public string? ResolveMiniName(VehicleTexture texture)
    {
        if (!string.IsNullOrEmpty(texture.TextureMini) && MiniPath(texture.TextureMini) != null)
            return texture.TextureMini;

        if (texture.Group != null && GroupsById.TryGetValue(texture.Group, out var grp)
            && !string.IsNullOrEmpty(grp.Mini))
            return grp.Mini;

        return texture.TextureMini;
    }

    public string? MiniForSkin(string? skinFile)
    {
        if (string.IsNullOrEmpty(skinFile)) return null;
        string key = Path.GetFileNameWithoutExtension(skinFile).ToLowerInvariant();
        return TextureBySkin.TryGetValue(key, out var tex) ? ResolveMiniName(tex) : null;
    }

    public VehicleTexture? TextureForSkin(string? skinFile)
    {
        if (string.IsNullOrEmpty(skinFile)) return null;
        string key = Path.GetFileNameWithoutExtension(skinFile).ToLowerInvariant();
        return TextureBySkin.TryGetValue(key, out var tex) ? tex : null;
    }

    public List<string> CollectMissingAssetLines()
    {
        var lines = new List<string>();
        foreach (var t in Textures)
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "dynamic",
                t.Directory.Replace('/', Path.DirectorySeparatorChar).TrimEnd('\\', '/'));
            string skin = Path.Combine(dir, t.Skinfile);
            if (!string.IsNullOrEmpty(t.Skinfile) && !File.Exists(skin) &&
                !File.Exists(Path.ChangeExtension(skin, ".mat")) &&
                !File.Exists(Path.ChangeExtension(skin, ".bmp")))
                lines.Add($"# no file: {t.Directory}{t.Skinfile}");

            if (!string.IsNullOrEmpty(t.Model))
            {
                string model = Path.Combine(dir, t.Model!);
                if (!File.Exists(model) && !File.Exists(Path.ChangeExtension(model, ".t3d")) &&
                    !File.Exists(Path.ChangeExtension(model, ".e3d")))
                    lines.Add($"# no model: {t.Directory}{t.Model}");
            }
        }
        return lines;
    }

    public string GroupHeader(string? groupId)
    {
        if (!string.IsNullOrEmpty(groupId) && GroupsById.TryGetValue(groupId, out var grp))
        {
            string mini = string.IsNullOrEmpty(grp.Mini) ? grp.Id : grp.Mini!;
            return string.IsNullOrEmpty(grp.Category) ? mini : $"{mini}  ({grp.Category})";
        }
        return groupId ?? "";
    }

    private static Dictionary<string, string>? _miniIndex;

    public static void PreloadMiniIndex(string miniDir = "textures/mini/") =>
        _miniIndex ??= BuildMiniIndex(miniDir);

    public static string? MiniPath(string? miniName, string miniDir = "textures/mini/")
    {
        if (string.IsNullOrEmpty(miniName)) return null;

        var index = _miniIndex ??= BuildMiniIndex(miniDir);
        return index.TryGetValue(miniName!.ToLowerInvariant(), out var path) ? path : null;
    }

    private static Dictionary<string, string> BuildMiniIndex(string miniDir)
    {
        var index = new Dictionary<string, string>();
        if (!Directory.Exists(miniDir))
            return index;

        foreach (string file in Directory.GetFiles(miniDir, "*.bmp"))
            index[Path.GetFileNameWithoutExtension(file).ToLowerInvariant()] = file;
        return index;
    }
}

public class VehicleSet
{
    [JsonPropertyName("uuid")] public string? Uuid { get; set; }
    [JsonPropertyName("mode")] public string? Mode { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("texture_refs")] public List<string> TextureRefs { get; set; } = new();
}

[JsonSerializable(typeof(VehicleEntry))]
[JsonSerializable(typeof(VehicleEntryCollection))]
internal partial class VehicleJsonContext : JsonSerializerContext
{
}
