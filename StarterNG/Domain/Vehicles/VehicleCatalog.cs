using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StarterNG.Application.Abstractions;

namespace StarterNG.Domain.Vehicles;

/// <summary>
/// Every livery the installation offers, indexed for the lookups the depot and
/// the consist editor make: by uuid, by skin file, and by the set a vehicle
/// belongs to.
/// </summary>
/// <remarks>
/// Was <c>VehicleCatalog</c>, which also owned file enumeration, textures.txt
/// parsing, a static thumbnail dictionary and a missing-asset scan. Those moved
/// to Infrastructure; what is left is the model and its resolution rules, filled
/// by a repository through <see cref="Ingest"/>.
/// </remarks>
public sealed class VehicleCatalog
{
    private readonly IMiniTextureIndex _minis;

    public VehicleCatalog(IMiniTextureIndex minis)
    {
        _minis = minis;
    }

    /// <summary>Every livery worth offering (wrecks excluded).</summary>
    public List<VehicleTexture> Textures { get; } = new();

    public Dictionary<string, VehicleGroup> GroupsById { get; } = new();

    public List<VehicleSet> Sets { get; } = new();

    public Dictionary<string, VehicleSet> SetByTextureUuid { get; } = new();

    public Dictionary<string, VehicleTexture> TextureByUuid { get; } = new();

    /// <summary>Liveries by skin file name without extension, lower cased.</summary>
    public Dictionary<string, VehicleTexture> TextureBySkin { get; } = new();

    public void BeginLoad()
    {
        Textures.Clear();
        GroupsById.Clear();
        Sets.Clear();
        SetByTextureUuid.Clear();
        TextureByUuid.Clear();
        TextureBySkin.Clear();
    }

    /// <summary>Adds one vehicle folder's liveries to the catalogue.</summary>
    public void Ingest(VehicleEntry entry)
    {
        foreach (var group in entry.Groups)
            if (!string.IsNullOrEmpty(group.Id))
                GroupsById.TryAdd(group.Id, group);

        foreach (var texture in entry.Textures)
        {
            if (!string.IsNullOrEmpty(texture.Uuid))
                TextureByUuid[texture.Uuid] = texture;

            if (!string.IsNullOrEmpty(texture.Skinfile))
                TextureBySkin.TryAdd(Path.GetFileNameWithoutExtension(texture.Skinfile).ToLowerInvariant(), texture);

            if (texture.Wreck)
                continue;

            Textures.Add(texture);
        }

        Sets.AddRange(entry.Sets);
    }

    /// <summary>Builds the indexes that depend on the catalogue being complete.</summary>
    public void EndLoad()
    {
        BuildSetIndex();
        ResolveGroupInheritance();
    }

    /// <summary>The vehicles of the fixed set this livery leads or belongs to.</summary>
    public List<VehicleTexture>? ResolveSet(VehicleTexture texture)
    {
        if (string.IsNullOrEmpty(texture.Uuid))
            return null;
        if (!SetByTextureUuid.TryGetValue(texture.Uuid, out var set) || set.TextureRefs is null)
            return null;

        var cars = new List<VehicleTexture>();
        foreach (string uuid in set.TextureRefs)
            if (!string.IsNullOrEmpty(uuid) && TextureByUuid.TryGetValue(uuid, out var car))
                cars.Add(car);

        return cars.Count > 0 ? cars : null;
    }

    /// <summary>
    /// True when the livery is a non-leading member of a set, and so is added
    /// along with its leader rather than on its own.
    /// </summary>
    public bool IsSetFollower(VehicleTexture texture)
    {
        if (string.IsNullOrEmpty(texture.Uuid))
            return false;
        if (!SetByTextureUuid.TryGetValue(texture.Uuid, out var set) ||
            set.TextureRefs is null || set.TextureRefs.Count < 2)
            return false;

        string? lead = set.TextureRefs.FirstOrDefault(reference => !string.IsNullOrEmpty(reference));
        return lead is not null && !string.Equals(lead, texture.Uuid, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The thumbnail to draw for a livery: its own if we have that bitmap,
    /// otherwise the one its group declares.
    /// </summary>
    public string? ResolveMiniName(VehicleTexture texture)
    {
        if (_minis.Has(texture.TextureMini))
            return texture.TextureMini;

        if (texture.Group is not null && GroupsById.TryGetValue(texture.Group, out var group) &&
            !string.IsNullOrEmpty(group.Mini))
            return group.Mini;

        return texture.TextureMini;
    }

    public string? MiniForSkin(string? skinFile) =>
        TextureForSkin(skinFile) is { } texture ? ResolveMiniName(texture) : null;

    public VehicleTexture? TextureForSkin(string? skinFile)
    {
        if (string.IsNullOrEmpty(skinFile))
            return null;

        string key = Path.GetFileNameWithoutExtension(skinFile).ToLowerInvariant();
        return TextureBySkin.TryGetValue(key, out var texture) ? texture : null;
    }

    /// <summary>Heading shown above a group in the depot browser.</summary>
    public string GroupHeader(string? groupId)
    {
        if (string.IsNullOrEmpty(groupId) || !GroupsById.TryGetValue(groupId, out var group))
            return groupId ?? "";

        string mini = string.IsNullOrEmpty(group.Mini) ? group.Id : group.Mini;
        return string.IsNullOrEmpty(group.Category) ? mini : $"{mini}  ({group.Category})";
    }

    private void BuildSetIndex()
    {
        foreach (var set in Sets)
        {
            if (set.TextureRefs is null)
                continue;

            foreach (string uuid in set.TextureRefs)
                if (!string.IsNullOrEmpty(uuid))
                    SetByTextureUuid[uuid] = set;
        }
    }

    /// <summary>
    /// Copies thumbnail, category and archived status down from each group onto
    /// its liveries, so the browser can sort and filter without walking back up.
    /// </summary>
    private void ResolveGroupInheritance()
    {
        foreach (var texture in Textures)
        {
            VehicleGroup? group = null;
            if (texture.Group is not null)
                GroupsById.TryGetValue(texture.Group, out group);

            string mini = group is not null && !string.IsNullOrEmpty(group.Mini)
                ? group.Mini
                : texture.MiniRef ?? "";

            string? category = group?.Category;

            // "*" means "categorise by the first letter of the thumbnail name".
            if (category == "*")
            {
                string source = !string.IsNullOrEmpty(mini) ? mini : texture.TextureMini ?? "";
                if (source.Length > 0)
                    category = char.ToUpperInvariant(source[0]).ToString();
            }

            texture.ResolvedCategory = category;
            texture.ResolvedClass = mini;
            texture.ResolvedArchived = group?.Archived ?? false;
        }
    }
}
