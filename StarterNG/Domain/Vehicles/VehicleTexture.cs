using System.Collections.Generic;

namespace StarterNG.Domain.Vehicles;

/// <summary>
/// One vehicle folder's contribution to the catalogue: the liveries it defines,
/// the groups they belong to and any fixed sets they form.
/// </summary>
public sealed class VehicleEntry
{
    public string? Uuid { get; set; }
    public List<VehicleGroup> Groups { get; } = new();
    public List<VehicleTexture> Textures { get; } = new();
    public List<VehicleSet> Sets { get; } = new();
}

/// <summary>
/// A family of liveries sharing a thumbnail and a category letter, as declared
/// by one textures.txt block.
/// </summary>
public sealed class VehicleGroup
{
    public string Id { get; set; } = "";
    public string? Category { get; set; }
    public string? Mini { get; set; }

    /// <summary>Withdrawn stock, hidden from the depot unless asked for.</summary>
    public bool Archived { get; set; }
}

/// <summary>A single livery: one skin file on one model.</summary>
public sealed class VehicleTexture
{
    public string? Uuid { get; set; }
    public string Directory { get; set; } = "";
    public string Skinfile { get; set; } = "";
    public string? Model { get; set; }
    public string? Group { get; set; }
    public string? MiniRef { get; set; }
    public string? TextureMini { get; set; }

    /// <summary>Wreck liveries exist in the files but are not offered in the depot.</summary>
    public bool Wreck { get; set; }

    public List<VehicleAlias> Aliases { get; } = new();
    public VehicleMeta? Meta { get; set; }

    public string FullPath => Directory + Skinfile;

    /// <summary>Thumbnail name after group inheritance; filled by the catalogue.</summary>
    public string ResolvedClass { get; internal set; } = "";

    /// <summary>Category letter after group inheritance; filled by the catalogue.</summary>
    public string? ResolvedCategory { get; internal set; }

    /// <summary>Whether the owning group is archived; filled by the catalogue.</summary>
    public bool ResolvedArchived { get; internal set; }
}

/// <summary>An alternative model this same skin can be worn by.</summary>
public sealed class VehicleAlias
{
    public string? Model { get; set; }
    public string? Group { get; set; }
    public string? MiniRef { get; set; }
    public string? TextureMini { get; set; }
}

/// <summary>
/// The comma separated description a livery carries after "//": who made it, for
/// which operator and depot, and when it was last revised.
/// </summary>
public sealed class VehicleMeta
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
    public List<string> Extra { get; } = new();
}

/// <summary>
/// Vehicles that belong together and are added to a consist as a unit, such as a
/// multiple unit or a fixed rake.
/// </summary>
public sealed class VehicleSet
{
    public string? Uuid { get; set; }
    public string? Mode { get; set; }
    public int Count { get; set; }
    public List<string> TextureRefs { get; set; } = new();
}
