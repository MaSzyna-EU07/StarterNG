using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StarterNG.Classes;

public static class LegacyTextureDatabase
{
    public static List<string> EnumerateFiles(string dynamicRoot = "dynamic")
    {
        var files = new List<string>();
        if (!Directory.Exists(dynamicRoot))
            return files;

        foreach (string group in Directory.EnumerateDirectories(dynamicRoot))
        foreach (string sub in Directory.EnumerateDirectories(group))
        {
            string path = Path.Combine(sub, "textures.txt");
            if (File.Exists(path))
                files.Add(path);
        }
        return files;
    }

    public static VehicleEntry? Parse(string path, string dynamicRoot = "dynamic")
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path, Encoding.GetEncoding(1250));
        }
        catch (Exception ex)
        {
            Infrastructure.Diagnostics.Log($"textures.txt {path}", ex);
            return null;
        }

        string dir = RelativeDirectory(path, dynamicRoot);
        var entry = new VehicleEntry { Uuid = "legacy:" + dir };

        bool archived = false;
        string categorySign = "*";
        int setSize = 0;
        var pending = new List<string>();

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;

            char c = line[0];
            if (c is '#' or '@' or '*') continue;

            if (line.StartsWith("$a", StringComparison.OrdinalIgnoreCase))
            {
                archived = true;
                continue;
            }

            if (c == '^')
            {
                FlushSet(entry, pending, setSize);
                setSize = line.Length > 1 && char.IsDigit(line[1]) ? line[1] - '0' : 0;
                continue;
            }

            if (line.StartsWith("!=", StringComparison.Ordinal))
            {
                categorySign = line.Length > 2 ? line[2].ToString() : "*";
                continue;
            }

            if (line.IndexOf('=') < 0) continue;
            if (line.StartsWith("//", StringComparison.Ordinal)) continue;

            var texture = ParseEntryLine(line, dir, categorySign, archived, entry);
            if (texture is null) continue;

            entry.Textures.Add(texture);

            if (setSize > 1)
            {
                pending.Add(texture.Uuid!);
                if (pending.Count == setSize)
                    FlushSet(entry, pending, setSize);
            }
        }

        FlushSet(entry, pending, setSize);
        return entry.Textures.Count > 0 ? entry : null;
    }

    private static void FlushSet(VehicleEntry entry, List<string> pending, int size)
    {
        if (pending.Count > 1)
            entry.Sets.Add(new VehicleSet
            {
                Uuid = "legacy-set:" + pending[0],
                Mode = "sequence",
                Count = size > 0 ? size : pending.Count,
                TextureRefs = new List<string>(pending)
            });
        pending.Clear();
    }

    private static VehicleTexture? ParseEntryLine(string line, string dir,
        string categorySign, bool archived, VehicleEntry entry)
    {
        string desc = "";
        int comment = line.IndexOf("//", StringComparison.Ordinal);
        if (comment >= 0)
        {
            desc = line[(comment + 2)..].Trim();
            line = line[..comment].Trim();
        }

        var parts = line.Split('=');
        if (parts.Length < 2) return null;

        string skin = StripExtension(parts[0].Trim());
        if (skin.Length == 0) return null;

        var specs = new List<(string Model, string Mini, string MiniD)>();
        for (int i = 1; i < parts.Length; i++)
        {
            var f = parts[i].Split(',');
            if (f.Length < 2) continue;
            specs.Add((f[0].Trim(), f[1].Trim(), f.Length > 2 ? f[2].Trim() : ""));
        }
        if (specs.Count == 0) return null;

        var first = specs[0];
        string groupId = $"legacy:{dir}:{first.Mini}:{categorySign}:{(archived ? 1 : 0)}";

        if (!entry.Groups.Exists(g => g.Id == groupId))
            entry.Groups.Add(new VehicleGroup
            {
                Id = groupId,
                Category = categorySign,
                Mini = first.Mini,
                Archived = archived,
                Implicit = true
            });

        var texture = new VehicleTexture
        {
            Uuid = $"legacy:{dir}{skin}",
            Directory = dir,
            Skinfile = skin,
            Model = first.Model,
            Group = groupId,
            MiniRef = first.Mini,
            TextureMini = string.IsNullOrEmpty(first.MiniD) ? null : first.MiniD,
            Meta = ParseDescription(desc)
        };

        for (int i = 1; i < specs.Count; i++)
            texture.Aliases.Add(new VehicleAlias
            {
                Model = specs[i].Model,
                MiniRef = specs[i].Mini,
                TextureMini = string.IsNullOrEmpty(specs[i].MiniD) ? null : specs[i].MiniD
            });

        return texture;
    }

    private static VehicleMeta? ParseDescription(string desc)
    {
        if (string.IsNullOrWhiteSpace(desc)) return null;

        var f = desc.Replace('_', ' ').Split(',');
        string At(int i) => i < f.Length ? f[i].Trim() : "";

        return new VehicleMeta
        {
            Raw = desc,
            Version = At(0),
            Vehicle = At(1),
            Operator = At(2),
            Depot = At(3),
            RevisionDate = At(4),
            RevisionPlace = At(5),
            TextureAuthor = At(6),
            PhotoAuthor = At(7)
        };
    }

    private static string StripExtension(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    private static string RelativeDirectory(string texturesPath, string dynamicRoot)
    {
        string dir = Path.GetDirectoryName(texturesPath) ?? "";
        string full = Path.GetFullPath(dir);
        string root = Path.GetFullPath(dynamicRoot);

        string rel = full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? full[root.Length..].TrimStart(Path.DirectorySeparatorChar, '/')
            : dir;

        return rel.Replace('\\', '/').TrimEnd('/') + "/";
    }
}
