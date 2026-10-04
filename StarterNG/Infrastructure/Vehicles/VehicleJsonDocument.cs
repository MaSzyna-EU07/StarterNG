using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StarterNG.Infrastructure.Vehicles;

// The vehicle database as it is written to disk (schema_version 1). Deliberately
// separate from the domain model: the model is free to change shape with the
// starter, the file format is not - packages written by someone else have to keep
// parsing. The property names are the ones the format shipped with, so a file
// written for the older reader still loads.
//
// Collections are nullable and left out when empty, which keeps a generated file
// close to what a person would have typed by hand.

public sealed class VehicleEntryDocument
{
    [JsonPropertyName("uuid")] public string? Uuid { get; set; }

    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }

    [JsonPropertyName("groups")] public List<VehicleGroupDocument>? Groups { get; set; }

    [JsonPropertyName("textures")] public List<VehicleTextureDocument>? Textures { get; set; }

    [JsonPropertyName("sets")] public List<VehicleSetDocument>? Sets { get; set; }

    [JsonPropertyName("unknown")] public List<string>? Unknown { get; set; }
}

/// <summary>Root of a merged database holding many entries in one file.</summary>
public sealed class VehicleCollectionDocument
{
    [JsonPropertyName("vehicles")] public List<VehicleEntryDocument>? Vehicles { get; set; }
}

public sealed class VehicleGroupDocument
{
    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("category")] public string? Category { get; set; }

    [JsonPropertyName("mini")] public string? Mini { get; set; }

    [JsonPropertyName("archived")] public bool Archived { get; set; }

    [JsonPropertyName("implicit")] public bool Implicit { get; set; }
}

public sealed class VehicleTextureDocument
{
    [JsonPropertyName("uuid")] public string? Uuid { get; set; }

    [JsonPropertyName("directory")] public string? Directory { get; set; }

    [JsonPropertyName("skinfile")] public string? Skinfile { get; set; }

    [JsonPropertyName("model")] public string? Model { get; set; }

    [JsonPropertyName("group")] public string? Group { get; set; }

    [JsonPropertyName("mini_ref")] public string? MiniRef { get; set; }

    [JsonPropertyName("texture_mini")] public string? TextureMini { get; set; }

    [JsonPropertyName("wreck")] public bool Wreck { get; set; }

    [JsonPropertyName("aliases")] public List<VehicleAliasDocument>? Aliases { get; set; }

    [JsonPropertyName("meta")] public VehicleMetaDocument? Meta { get; set; }
}

public sealed class VehicleAliasDocument
{
    [JsonPropertyName("model")] public string? Model { get; set; }

    [JsonPropertyName("group")] public string? Group { get; set; }

    [JsonPropertyName("mini_ref")] public string? MiniRef { get; set; }

    [JsonPropertyName("texture_mini")] public string? TextureMini { get; set; }
}

public sealed class VehicleMetaDocument
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

    [JsonPropertyName("extra")] public List<string>? Extra { get; set; }
}

public sealed class VehicleSetDocument
{
    [JsonPropertyName("uuid")] public string? Uuid { get; set; }

    [JsonPropertyName("mode")] public string? Mode { get; set; }

    [JsonPropertyName("count")] public int Count { get; set; }

    [JsonPropertyName("texture_refs")] public List<string>? TextureRefs { get; set; }
}

// Source-generated metadata: PublishAot and PublishTrimmed rule out the reflection
// based serializer. The generator pulls in every nested document type on its own,
// and the reader's leniency is set on the options in VehicleJsonSerializer.
[JsonSerializable(typeof(VehicleEntryDocument))]
[JsonSerializable(typeof(VehicleCollectionDocument))]
internal partial class VehicleJsonContext : JsonSerializerContext
{
}
