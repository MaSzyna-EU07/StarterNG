using System.Collections.Generic;

namespace StarterNG.Domain.Vehicles;

public sealed class VehicleEntry
{
    public string? Uuid { get; set; }
    public List<VehicleGroup> Groups { get; } = new();
    public List<VehicleTexture> Textures { get; } = new();
    public List<VehicleSet> Sets { get; } = new();
}

public sealed class VehicleGroup
{
    public string Id { get; set; } = "";
    public string? Category { get; set; }
    public string? Mini { get; set; }

    public bool Archived { get; set; }
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
