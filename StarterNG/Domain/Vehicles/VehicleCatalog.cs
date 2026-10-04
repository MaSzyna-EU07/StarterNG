using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StarterNG.Application.Abstractions;

namespace StarterNG.Domain.Vehicles;

public sealed class VehicleCatalog
{
    private readonly IMiniTextureIndex _minis;

    public VehicleCatalog(IMiniTextureIndex minis)
    {
        _minis = minis;
    }

    public List<VehicleTexture> Textures { get; } = new();

    public Dictionary<string, VehicleGroup> GroupsById { get; } = new();

    public List<VehicleSet> Sets { get; } = new();

    public Dictionary<string, VehicleSet> SetByTextureUuid { get; } = new();

    public Dictionary<string, VehicleTexture> TextureByUuid { get; } = new();

    public Dictionary<string, VehicleTexture> TextureBySkin { get; } = new();

    /// <summary>
    /// The way the old starter matched a scenery vehicle: folder, skin and model.
    /// A skin name recurs across folders ("red" for a Clio and a crane), and one
    /// skin may dress several models (both halves of the ET42-026).
    /// </summary>
    private readonly Dictionary<string, VehicleTexture> _textureByFolderSkinModel = new();

    private readonly Dictionary<string, VehicleTexture> _textureByFolderSkin = new();

    private readonly Dictionary<string, VehicleTexture> _textureBySkinModel = new();

    public void BeginLoad()
    {
        Textures.Clear();
        GroupsById.Clear();
        Sets.Clear();
        SetByTextureUuid.Clear();
        TextureByUuid.Clear();
        TextureBySkin.Clear();
        _textureByFolderSkinModel.Clear();
        _textureByFolderSkin.Clear();
        _textureBySkinModel.Clear();
    }

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
            {
                TextureBySkin.TryAdd(Key(texture.Skinfile), texture);
                _textureByFolderSkinModel.TryAdd(FolderSkinModelKey(texture.Directory, texture.Skinfile, texture.Model), texture);
                _textureByFolderSkin.TryAdd(FolderSkinKey(texture.Directory, texture.Skinfile), texture);
                _textureBySkinModel.TryAdd(SkinModelKey(texture.Skinfile, texture.Model), texture);
            }

            if (texture.Wreck)
                continue;

            Textures.Add(texture);
        }

        Sets.AddRange(entry.Sets);
    }

    public void EndLoad()
    {
        BuildSetIndex();
        ResolveGroupInheritance();
    }

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

    public string? ResolveMiniName(VehicleTexture texture)
    {
        if (_minis.Has(texture.TextureMini))
            return texture.TextureMini;

        if (texture.Group is not null && GroupsById.TryGetValue(texture.Group, out var group) &&
            !string.IsNullOrEmpty(group.Mini))
            return group.Mini;

        return texture.TextureMini;
    }

    public string? MiniFor(string? dataFolder, string? skinFile, string? model) =>
        TextureFor(dataFolder, skinFile, model) is { } texture ? ResolveMiniName(texture) : null;

    /// <summary>
    /// The texture a scenery vehicle wears, matched like the old starter did: by
    /// folder, skin and model, then by less of it for a vehicle that moved.
    /// </summary>
    public VehicleTexture? TextureFor(string? dataFolder, string? skinFile, string? model)
    {
        if (string.IsNullOrEmpty(skinFile))
            return null;

        if (_textureByFolderSkinModel.TryGetValue(FolderSkinModelKey(dataFolder, skinFile, model), out var texture) ||
            _textureByFolderSkin.TryGetValue(FolderSkinKey(dataFolder, skinFile), out texture) ||
            _textureBySkinModel.TryGetValue(SkinModelKey(skinFile, model), out texture))
            return texture;

        return TextureForSkin(skinFile);
    }

    public VehicleTexture? TextureForSkin(string? skinFile)
    {
        if (string.IsNullOrEmpty(skinFile))
            return null;

        return TextureBySkin.TryGetValue(Key(skinFile), out var texture) ? texture : null;
    }

    private static string Key(string skinFile) => Path.GetFileNameWithoutExtension(skinFile).ToLowerInvariant();

    private static string SkinModelKey(string skinFile, string? model) =>
        Key(skinFile) + "|" + (string.IsNullOrEmpty(model) ? "" : Path.GetFileNameWithoutExtension(model).ToLowerInvariant());

    private static string FolderSkinKey(string? folder, string skinFile) => Folder(folder) + "|" + Key(skinFile);

    private static string FolderSkinModelKey(string? folder, string skinFile, string? model) =>
        Folder(folder) + "|" + SkinModelKey(skinFile, model);

    /// <summary>"PKP\ET42_V2" in a scenery, "pkp/et42_v2/" in the catalogue.</summary>
    private static string Folder(string? folder) =>
        (folder ?? "").Replace('\\', '/').Trim('/').ToLowerInvariant();

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
