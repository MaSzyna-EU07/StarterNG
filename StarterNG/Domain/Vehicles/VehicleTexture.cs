using System.Collections.Generic;

namespace StarterNG.Domain.Vehicles;

public sealed class VehicleEntry
{
    public string? Uuid { get; set; }

    /// <summary>
    /// Version of the file format the entry was read from, zero when it came from
    /// a legacy textures.txt. Kept so a file written by a newer starter is refused
    /// outright instead of being read with half its meaning missing.
    /// </summary>
    public int SchemaVersion { get; set; }

    public List<VehicleGroup> Groups { get; } = new();
    public List<VehicleTexture> Textures { get; } = new();
    public List<VehicleSet> Sets { get; } = new();

    /// <summary>
    /// Source lines the reader did not turn into anything else - legacy headers,
    /// comments and malformed liveries - kept verbatim so an entry written back
    /// out does not quietly drop what its author put in the file. Content only:
    /// blank lines and the original spacing are not preserved.
    /// </summary>
    public List<string> Unknown { get; } = new();
}

public sealed class VehicleGroup
{
    public string Id { get; set; } = "";
    public string? Category { get; set; }
    public string? Mini { get; set; }

    public bool Archived { get; set; }

    /// <summary>
    /// True when nothing declared the group and it was inferred from the liveries
    /// that share a thumbnail - which is every group textures.txt produces, as the
    /// legacy format has no syntax for declaring one.
    /// </summary>
    public bool Implicit { get; set; }
}

public sealed class VehicleTexture
{
    public string? Uuid { get; set; }
    public string Directory { get; set; } = "";
    public string Skinfile { get; set; } = "";
    public string? Model { get; set; }
    public string? Group { get; set; }
    public string? MiniRef { get; set; }
    public string? TextureMini { get; set; }

    public bool Wreck { get; set; }

    public List<VehicleAlias> Aliases { get; } = new();
    public VehicleMeta? Meta { get; set; }

    public string FullPath => Directory + Skinfile;

    public string ResolvedClass { get; internal set; } = "";

    public string? ResolvedCategory { get; internal set; }

    public bool ResolvedArchived { get; internal set; }
}

public sealed class VehicleAlias
{
    public string? Model { get; set; }
    public string? Group { get; set; }
    public string? MiniRef { get; set; }
    public string? TextureMini { get; set; }
}

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

public sealed class VehicleSet
{
    public string? Uuid { get; set; }
    public string? Mode { get; set; }
    public int Count { get; set; }
    public List<string> TextureRefs { get; set; } = new();
}
