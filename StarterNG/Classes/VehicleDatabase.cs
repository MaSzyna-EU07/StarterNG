using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Loads the database. When a merged vehicles.json is present it is used,
    /// otherwise every per-vehicle *.json file in the directory is read.
    /// Never throws: unreadable files are skipped.
    /// </summary>
    public void Load(string directory = "databases/vehicles/")
    {
        Textures.Clear();
        GroupsById.Clear();
        Sets.Clear();
        SetByTextureUuid.Clear();

        if (!Directory.Exists(directory))
            return;

        string merged = Path.Combine(directory, "vehicles.json");
        if (File.Exists(merged))
        {
            LoadMerged(merged);
        }
        else
        {
            foreach (string file in Directory.GetFiles(directory, "*.json"))
                LoadEntryFile(file);
        }

        BuildSetIndex();
    }

    private void LoadMerged(string file)
    {
        try
        {
            var collection = JsonSerializer.Deserialize<VehicleEntryCollection>(
                File.ReadAllText(file), JsonOpts);
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
            var entry = JsonSerializer.Deserialize<VehicleEntry>(
                File.ReadAllText(file), JsonOpts);
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
            // skip wrecks from the standard browser list
            if (texture.Wreck) continue;
            Textures.Add(texture);
        }

        Sets.AddRange(entry.Sets);
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

    /// <summary>Best miniature name for a texture (texture_mini -> mini_ref -> group mini).</summary>
    public string? ResolveMiniName(VehicleTexture texture)
    {
        if (!string.IsNullOrEmpty(texture.TextureMini)) return texture.TextureMini;
        if (!string.IsNullOrEmpty(texture.MiniRef)) return texture.MiniRef;
        if (texture.Group is not null && GroupsById.TryGetValue(texture.Group, out var grp))
            return grp.Mini;
        return null;
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

    /// <summary>Resolves a miniature .bmp path under textures/mini/, or null if missing.</summary>
    public static string? MiniPath(string? miniName, string miniDir = "textures/mini/")
    {
        if (string.IsNullOrEmpty(miniName)) return null;
        string path = Path.Combine(miniDir, miniName + ".bmp");
        return File.Exists(path) ? path : null;
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
