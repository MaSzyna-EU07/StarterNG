using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace StarterNG.Classes;

// =====================================================================
//  Vehicle JSON database (databases/vehicles/*.json)
//  Format documented in vehicleEntryDoc.md (schema_version 1).
// =====================================================================

/// <summary>Root object of a single vehicle JSON file.</summary>
public class VehicleEntry
{
    [JsonPropertyName("uuid")] public string? Uuid { get; set; }
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("groups")] public List<VehicleGroup> Groups { get; set; } = new();
    [JsonPropertyName("textures")] public List<VehicleTexture> Textures { get; set; } = new();
    [JsonPropertyName("sets")] public List<VehicleSet> Sets { get; set; } = new();
    [JsonPropertyName("unknown")] public List<string> Unknown { get; set; } = new();
}

/// <summary>Merged database root (databases/vehicles/vehicles.json).</summary>
public class VehicleEntryCollection
{
    [JsonPropertyName("vehicles")] public List<VehicleEntry> Vehicles { get; set; } = new();
}

/// <summary>Legacy header / vehicle category group.</summary>
public class VehicleGroup
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("mini")] public string? Mini { get; set; }
    [JsonPropertyName("archived")] public bool Archived { get; set; }
    [JsonPropertyName("implicit")] public bool Implicit { get; set; }
}

/// <summary>A single .mat skin entry.</summary>
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

    /// <summary>Full skin path = directory + skinfile.</summary>
    [JsonIgnore] public string FullPath => Directory + Skinfile;

    // ── cached lookups, populated once after load (VehicleDatabase.BuildTextureIndex) ──
    // These mirror the depot's ClassOf / CategoryOf so the search/filter hot path
    // never recomputes them (no per-keystroke dictionary lookups over every texture).

    /// <summary>Group's "mini" (or this texture's mini_ref) — the vehicle class. Never null.</summary>
    [JsonIgnore] public string ResolvedClass { get; internal set; } = "";

    /// <summary>Group's "category" letter, or null when the group is unknown.</summary>
    [JsonIgnore] public string? ResolvedCategory { get; internal set; }
}

/// <summary>Alternate mapping from a malformed multi-= legacy line.</summary>
public class VehicleAlias
{
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("group")] public string? Group { get; set; }
    [JsonPropertyName("mini_ref")] public string? MiniRef { get; set; }
    [JsonPropertyName("texture_mini")] public string? TextureMini { get; set; }
}

/// <summary>Parsed metadata from the legacy // comment section.</summary>
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

/// <summary>
/// Aggregated, in-memory view over every vehicle JSON file found in
/// databases/vehicles/. Provides lookup helpers for the depot UI.
/// </summary>
public class VehicleDatabase
{
    public List<VehicleTexture> Textures { get; } = new();
    public Dictionary<string, VehicleGroup> GroupsById { get; } = new();
    public List<VehicleSet> Sets { get; } = new();

    /// <summary>Texture-uuid -> automatic-consist set that contains it.</summary>
    public Dictionary<string, VehicleSet> SetByTextureUuid { get; } = new();

    /// <summary>Texture-uuid -> texture (includes wrecks, so set refs resolve).</summary>
    public Dictionary<string, VehicleTexture> TextureByUuid { get; } = new();

    /// <summary>skinfile (without extension, lower-case) -> texture.</summary>
    public Dictionary<string, VehicleTexture> TextureBySkin { get; } = new();

    // System.Text.Json options matching the previous Newtonsoft leniency: property
    // names matched case-insensitively, // and /* */ comments skipped, and trailing
    // commas allowed. Unknown members are ignored by default. The source-generated
    // VehicleJsonContext resolver keeps deserialization AOT/trim-safe (no reflection).
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        TypeInfoResolver = VehicleJsonContext.Default,
    };

    /// <summary>
    /// Loads the database in one call. When a merged vehicles.json is present
    /// it is used, otherwise every per-vehicle *.json file is read.
    /// Never throws: unreadable files are skipped.
    /// </summary>
    public void Load(string directory = "databases/vehicles/")
    {
        BeginLoad();
        foreach (string file in EnumerateFiles(directory))
            LoadFile(file);
        EndLoad();
    }

    /// <summary>Resets the aggregate before an incremental load.</summary>
    public void BeginLoad()
    {
        Textures.Clear();
        GroupsById.Clear();
        Sets.Clear();
        SetByTextureUuid.Clear();
        TextureByUuid.Clear();
        TextureBySkin.Clear();
    }

    /// <summary>Finalises an incremental load (builds lookup indexes).</summary>
    public void EndLoad()
    {
        BuildSetIndex();
        BuildTextureIndex();
    }

    /// <summary>
    /// Files that make up the database: the merged vehicles.json if present,
    /// otherwise every per-vehicle *.json. Used to drive load progress.
    /// </summary>
    public static List<string> EnumerateFiles(string directory = "databases/vehicles/")
    {
        if (!Directory.Exists(directory))
            return new List<string>();

        string merged = Path.Combine(directory, "vehicles.json");
        if (File.Exists(merged))
            return new List<string> { merged };

        return Directory.GetFiles(directory, "*.json").ToList();
    }

    /// <summary>Ingests a single database file (merged or per-vehicle).</summary>
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
            // ignore malformed merged database
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
            // ignore malformed single-vehicle file
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
            // index every texture (wrecks too) so set / skin references resolve
            if (!string.IsNullOrEmpty(texture.Uuid))
                TextureByUuid[texture.Uuid!] = texture;
            if (!string.IsNullOrEmpty(texture.Skinfile))
                TextureBySkin.TryAdd(Path.GetFileNameWithoutExtension(texture.Skinfile).ToLowerInvariant(), texture);

            // skip wrecks from the standard browser list
            if (texture.Wreck) continue;
            Textures.Add(texture);
        }

        Sets.AddRange(entry.Sets);
    }

    /// <summary>
    /// If the texture belongs to an automatic-consist set, returns all of that
    /// set's textures in their defined order; otherwise null.
    /// </summary>
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

    // Resolves each texture's class (group mini / mini_ref) and category letter
    // once, after all groups are loaded, so the depot reads cached fields instead
    // of doing a GroupsById lookup per texture on every search keystroke.
    private void BuildTextureIndex()
    {
        foreach (var t in Textures)
        {
            VehicleGroup? g = null;
            if (t.Group != null)
                GroupsById.TryGetValue(t.Group, out g);

            t.ResolvedCategory = g?.Category;
            t.ResolvedClass = g != null && !string.IsNullOrEmpty(g.Mini)
                ? g.Mini!
                : t.MiniRef ?? "";
        }
    }

    /// <summary>
    /// Miniature name for a texture: the texture_mini property, but if no .bmp
    /// for it exists, falls back to the mini of the group it belongs to.
    /// </summary>
    public string? ResolveMiniName(VehicleTexture texture)
    {
        if (!string.IsNullOrEmpty(texture.TextureMini) && MiniPath(texture.TextureMini) != null)
            return texture.TextureMini;

        if (texture.Group != null && GroupsById.TryGetValue(texture.Group, out var grp)
            && !string.IsNullOrEmpty(grp.Mini))
            return grp.Mini;

        return texture.TextureMini;
    }

    /// <summary>Resolved mini for a skin file (matched case-insensitively), or null.</summary>
    public string? MiniForSkin(string? skinFile)
    {
        if (string.IsNullOrEmpty(skinFile)) return null;
        string key = Path.GetFileNameWithoutExtension(skinFile).ToLowerInvariant();
        return TextureBySkin.TryGetValue(key, out var tex) ? ResolveMiniName(tex) : null;
    }

    /// <summary>The texture for a skin file (matched case-insensitively), or null.</summary>
    public VehicleTexture? TextureForSkin(string? skinFile)
    {
        if (string.IsNullOrEmpty(skinFile)) return null;
        string key = Path.GetFileNameWithoutExtension(skinFile).ToLowerInvariant();
        return TextureBySkin.TryGetValue(key, out var tex) ? tex : null;
    }

    /// <summary>Header label for the group a texture belongs to.</summary>
    public string GroupHeader(string? groupId)
    {
        if (!string.IsNullOrEmpty(groupId) && GroupsById.TryGetValue(groupId, out var grp))
        {
            string mini = string.IsNullOrEmpty(grp.Mini) ? grp.Id : grp.Mini!;
            return string.IsNullOrEmpty(grp.Category) ? mini : $"{mini}  ({grp.Category})";
        }
        return groupId ?? "";
    }

    // Case-insensitive index of mini .bmp files (built once).
    private static Dictionary<string, string>? _miniIndex;

    /// <summary>
    /// Builds the miniature .bmp index up front (called from the startup load,
    /// behind the splash) so the first thumbnail render never stalls the UI
    /// thread scanning textures/mini/. No-op if it was already built.
    /// </summary>
    public static void PreloadMiniIndex(string miniDir = "textures/mini/") =>
        _miniIndex ??= BuildMiniIndex(miniDir);

    /// <summary>
    /// Resolves a miniature .bmp path under textures/mini/ case-insensitively,
    /// or null if missing. The directory is indexed once and reused.
    /// </summary>
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

/// <summary>Automatic consist definition (legacy ^x grouping).</summary>
public class VehicleSet
{
    [JsonPropertyName("uuid")] public string? Uuid { get; set; }
    [JsonPropertyName("mode")] public string? Mode { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("texture_refs")] public List<string> TextureRefs { get; set; } = new();
}

// Source-generated JSON metadata — makes deserialization AOT/trim-safe by avoiding
// runtime reflection over the model types. Nested types (VehicleGroup, VehicleTexture,
// VehicleSet, VehicleAlias, VehicleMeta) are pulled in automatically by the generator.
[JsonSerializable(typeof(VehicleEntry))]
[JsonSerializable(typeof(VehicleEntryCollection))]
internal partial class VehicleJsonContext : JsonSerializerContext
{
}
