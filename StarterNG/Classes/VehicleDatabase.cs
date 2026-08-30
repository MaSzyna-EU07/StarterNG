using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StarterNG.Classes;

public class VehicleEntry
{
    public string? Uuid { get; set; }
    public List<VehicleGroup> Groups { get; set; } = new();
    public List<VehicleTexture> Textures { get; set; } = new();
    public List<VehicleSet> Sets { get; set; } = new();
}

public class VehicleGroup
{
    public string Id { get; set; } = "";
    public string? Category { get; set; }
    public string? Mini { get; set; }
    public bool Archived { get; set; }
}

public class VehicleTexture
{
    public string? Uuid { get; set; }
    public string Directory { get; set; } = "";
    public string Skinfile { get; set; } = "";
    public string? Model { get; set; }
    public string? Group { get; set; }
    public string? MiniRef { get; set; }
    public string? TextureMini { get; set; }
    public bool Wreck { get; set; }
    public List<VehicleAlias> Aliases { get; set; } = new();
    public VehicleMeta? Meta { get; set; }

    public string FullPath => Directory + Skinfile;

    public string ResolvedClass { get; internal set; } = "";

    public string? ResolvedCategory { get; internal set; }

    public bool ResolvedArchived { get; internal set; }
}

public class VehicleAlias
{
    public string? Model { get; set; }
    public string? Group { get; set; }
    public string? MiniRef { get; set; }
    public string? TextureMini { get; set; }
}

public class VehicleMeta
{
    public string? Raw { get; set; }
    public string? Version { get; set; }
    public string? Vehicle { get; set; }
    public string? Operator { get; set; }
    public string? Depot { get; set; }
    public string? RevisionDate { get; set; }
    public string? RevisionPlace { get; set; }
    public string? TextureAuthor { get; set; }
    public string? PhotoAuthor { get; set; }
    public List<string> Extra { get; set; } = new();
}

public class VehicleDatabase
{
    public List<VehicleTexture> Textures { get; } = new();
    public Dictionary<string, VehicleGroup> GroupsById { get; } = new();
    public List<VehicleSet> Sets { get; } = new();

    public Dictionary<string, VehicleSet> SetByTextureUuid { get; } = new();

    public Dictionary<string, VehicleTexture> TextureByUuid { get; } = new();

    public Dictionary<string, VehicleTexture> TextureBySkin { get; } = new();

    public int LoadFromTexturesTxt(string dynamicRoot = "dynamic")
    {
        BeginLoad();

        int count = 0;
        foreach (string file in TexturesTxt.EnumerateFiles(dynamicRoot))
        {
            var entry = TexturesTxt.Parse(file, dynamicRoot);
            if (entry is null) continue;

            count += entry.Textures.Count;
            Ingest(entry);
        }

        EndLoad();
        return count;
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
        if (HasMini(texture.TextureMini))
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
        var index = _miniIndex ??= BuildMiniIndex(miniDir);

        if (!string.IsNullOrEmpty(miniName) &&
            index.TryGetValue(miniName!.ToLowerInvariant(), out var path))
            return path;

        return FallbackMiniPath(miniDir);
    }

    public static bool HasMini(string? miniName, string miniDir = "textures/mini/")
    {
        if (string.IsNullOrEmpty(miniName)) return false;
        var index = _miniIndex ??= BuildMiniIndex(miniDir);
        return index.ContainsKey(miniName!.ToLowerInvariant());
    }

    public static string? FallbackMiniPath(string miniDir = "textures/mini/")
    {
        var index = _miniIndex ??= BuildMiniIndex(miniDir);
        return index.TryGetValue("other", out var other) ? other : null;
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
    public string? Uuid { get; set; }
    public string? Mode { get; set; }
    public int Count { get; set; }
    public List<string> TextureRefs { get; set; } = new();
}
