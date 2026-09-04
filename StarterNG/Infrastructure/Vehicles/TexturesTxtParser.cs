using System;
using System.Collections.Generic;
using StarterNG.Domain.Vehicles;

namespace StarterNG.Infrastructure.Vehicles;

public sealed class TexturesTxtParser
{
    private const string DeriveCategory = "*";

    public VehicleEntry? Parse(string directory, IReadOnlyList<string> lines)
    {
        var entry = new VehicleEntry { Uuid = "legacy:" + directory };

        bool archived = false;
        string categorySign = DeriveCategory;
        int setSize = 0;
        var pendingSet = new List<string>();

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0)
                continue;

            char first = line[0];
            if (first is '#' or '@' or '*')
                continue;

            if (line.StartsWith("$a", StringComparison.OrdinalIgnoreCase))
            {
                archived = true;
                continue;
            }

            if (first == '^')
            {
                FlushSet(entry, pendingSet, setSize);
                setSize = line.Length > 1 && char.IsDigit(line[1]) ? line[1] - '0' : 0;
                continue;
            }

            if (line.StartsWith("!=", StringComparison.Ordinal))
            {
                categorySign = line.Length > 2 ? line[2].ToString() : DeriveCategory;
                continue;
            }

            if (line.IndexOf('=') < 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            var texture = ParseLivery(line, directory, categorySign, archived, entry);
            if (texture is null)
                continue;

            entry.Textures.Add(texture);

            if (setSize > 1)
            {
                pendingSet.Add(texture.Uuid!);
                if (pendingSet.Count == setSize)
                    FlushSet(entry, pendingSet, setSize);
            }
        }

        FlushSet(entry, pendingSet, setSize);
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

    private static VehicleTexture? ParseLivery(string line, string directory, string categorySign, bool archived,
                                               VehicleEntry entry)
    {
        string description = "";
        int comment = line.IndexOf("//", StringComparison.Ordinal);
        if (comment >= 0)
        {
            description = line[(comment + 2)..].Trim();
            line = line[..comment].Trim();
        }

        string[] parts = line.Split('=');
        if (parts.Length < 2)
            return null;

        string skin = StripExtension(parts[0].Trim());
        if (skin.Length == 0)
            return null;

        var specs = new List<(string Model, string Mini, string TextureMini)>();
        for (int i = 1; i < parts.Length; i++)
        {
            string[] fields = parts[i].Split(',');
            if (fields.Length < 2)
                continue;
            specs.Add((fields[0].Trim(), fields[1].Trim(), fields.Length > 2 ? fields[2].Trim() : ""));
        }
        if (specs.Count == 0)
            return null;

        var lead = specs[0];
        string groupId = $"legacy:{directory}:{lead.Mini}:{categorySign}:{(archived ? 1 : 0)}";

        if (!entry.Groups.Exists(group => group.Id == groupId))
            entry.Groups.Add(new VehicleGroup
            {
                Id = groupId,
                Category = categorySign,
                Mini = lead.Mini,
                Archived = archived
            });

        var texture = new VehicleTexture
        {
            Uuid = $"legacy:{directory}{skin}",
            Directory = directory,
            Skinfile = skin,
            Wreck = skin.Contains("wreck", StringComparison.OrdinalIgnoreCase) ||
                    skin.Contains("wrak", StringComparison.OrdinalIgnoreCase),
            Model = lead.Model,
            Group = groupId,
            MiniRef = lead.Mini,
            TextureMini = string.IsNullOrEmpty(lead.TextureMini) ? null : lead.TextureMini,
            Meta = ParseDescription(description)
        };

        for (int i = 1; i < specs.Count; i++)
            texture.Aliases.Add(new VehicleAlias
            {
                Model = specs[i].Model,
                MiniRef = specs[i].Mini,
                TextureMini = string.IsNullOrEmpty(specs[i].TextureMini) ? null : specs[i].TextureMini
            });

        return texture;
    }

    private static VehicleMeta? ParseDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        string[] fields = description.Replace('_', ' ').Split(',');
        string At(int index) => index < fields.Length ? fields[index].Trim() : "";

        return new VehicleMeta
        {
            Raw = description,
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
}
