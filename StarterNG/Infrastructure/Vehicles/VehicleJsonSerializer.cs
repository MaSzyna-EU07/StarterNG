using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using StarterNG.Domain.Vehicles;

namespace StarterNG.Infrastructure.Vehicles;

/// <summary>
/// Reads and writes <see cref="VehicleEntry"/> in the JSON database format,
/// mapping it on and off the document types that define the format.
/// </summary>
public sealed class VehicleJsonSerializer
{
    /// <summary>The format this build writes, and the newest one it can read.</summary>
    public const int SchemaVersion = 1;

    // Leniency a hand-written package file deserves: names matched case
    // insensitively, // and /* */ comments skipped, trailing commas allowed. The
    // relaxed encoder keeps Polish text in the credits and in the preserved legacy
    // comments readable instead of turning it into \uXXXX escapes, and the
    // source-generated resolver keeps all of it AOT-safe.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = VehicleJsonContext.Default
    };

    private static readonly JsonTypeInfo<VehicleEntryDocument> EntryInfo =
        (JsonTypeInfo<VehicleEntryDocument>)Options.GetTypeInfo(typeof(VehicleEntryDocument));

    private static readonly JsonTypeInfo<VehicleCollectionDocument> CollectionInfo =
        (JsonTypeInfo<VehicleCollectionDocument>)Options.GetTypeInfo(typeof(VehicleCollectionDocument));

    public string Write(VehicleEntry entry) => JsonSerializer.Serialize(ToDocument(entry), EntryInfo);

    public string WriteCollection(IEnumerable<VehicleEntry> entries) =>
        JsonSerializer.Serialize(
            new VehicleCollectionDocument { Vehicles = entries.Select(ToDocument).ToList() },
            CollectionInfo);

    /// <summary>One entry per file, the form a package is authored in.</summary>
    public VehicleEntry? Read(string json)
    {
        var document = JsonSerializer.Deserialize(json, EntryInfo);
        return document is null ? null : FromDocument(document);
    }

    /// <summary>Many entries in one file, the form a whole database is shipped in.</summary>
    public List<VehicleEntry> ReadCollection(string json)
    {
        var document = JsonSerializer.Deserialize(json, CollectionInfo);
        if (document?.Vehicles is null)
            return new List<VehicleEntry>();

        return document.Vehicles.Select(FromDocument).ToList();
    }

    /// <summary>
    /// Whether this build understands the file. An entry written by a newer
    /// starter is refused rather than half-read, an entry with no version at all
    /// is taken as the current one - the format carried no version when the first
    /// files were written by hand.
    /// </summary>
    public static bool IsReadable(VehicleEntry entry) => entry.SchemaVersion <= SchemaVersion;

    private static VehicleEntryDocument ToDocument(VehicleEntry entry) =>
        new()
        {
            Uuid = entry.Uuid,
            SchemaVersion = SchemaVersion,
            Groups = OrNull(entry.Groups.Select(group => new VehicleGroupDocument
            {
                Id = group.Id,
                Category = group.Category,
                Mini = group.Mini,
                Archived = group.Archived,
                Implicit = group.Implicit
            }).ToList()),
            Textures = OrNull(entry.Textures.Select(ToDocument).ToList()),
            Sets = OrNull(entry.Sets.Select(set => new VehicleSetDocument
            {
                Uuid = set.Uuid,
                Mode = set.Mode,
                Count = set.Count,
                TextureRefs = OrNull(set.TextureRefs)
            }).ToList()),
            Unknown = OrNull(entry.Unknown)
        };

    private static VehicleTextureDocument ToDocument(VehicleTexture texture) =>
        new()
        {
            Uuid = texture.Uuid,
            Directory = texture.Directory,
            Skinfile = texture.Skinfile,
            Model = texture.Model,
            Group = texture.Group,
            MiniRef = texture.MiniRef,
            TextureMini = texture.TextureMini,
            Wreck = texture.Wreck,
            Aliases = OrNull(texture.Aliases.Select(alias => new VehicleAliasDocument
            {
                Model = alias.Model,
                Group = alias.Group,
                MiniRef = alias.MiniRef,
                TextureMini = alias.TextureMini
            }).ToList()),
            Meta = texture.Meta is null
                ? null
                : new VehicleMetaDocument
                {
                    Raw = texture.Meta.Raw,
                    Version = texture.Meta.Version,
                    Vehicle = texture.Meta.Vehicle,
                    Operator = texture.Meta.Operator,
                    Depot = texture.Meta.Depot,
                    RevisionDate = texture.Meta.RevisionDate,
                    RevisionPlace = texture.Meta.RevisionPlace,
                    TextureAuthor = texture.Meta.TextureAuthor,
                    PhotoAuthor = texture.Meta.PhotoAuthor,
                    Extra = OrNull(texture.Meta.Extra)
                }
        };

    private static VehicleEntry FromDocument(VehicleEntryDocument document)
    {
        var entry = new VehicleEntry
        {
            Uuid = document.Uuid,
            SchemaVersion = document.SchemaVersion == 0 ? SchemaVersion : document.SchemaVersion
        };

        foreach (var group in document.Groups ?? Enumerable.Empty<VehicleGroupDocument>())
            entry.Groups.Add(new VehicleGroup
            {
                Id = group.Id ?? "",
                Category = group.Category,
                Mini = group.Mini,
                Archived = group.Archived,
                Implicit = group.Implicit
            });

        foreach (var texture in document.Textures ?? Enumerable.Empty<VehicleTextureDocument>())
            entry.Textures.Add(FromDocument(texture));

        foreach (var set in document.Sets ?? Enumerable.Empty<VehicleSetDocument>())
            entry.Sets.Add(new VehicleSet
            {
                Uuid = set.Uuid,
                Mode = set.Mode,
                Count = set.Count,
                TextureRefs = set.TextureRefs ?? new List<string>()
            });

        if (document.Unknown is not null)
            entry.Unknown.AddRange(document.Unknown);

        return entry;
    }

    private static VehicleTexture FromDocument(VehicleTextureDocument document)
    {
        var texture = new VehicleTexture
        {
            Uuid = document.Uuid,
            Directory = document.Directory ?? "",
            Skinfile = document.Skinfile ?? "",
            Model = document.Model,
            Group = document.Group,
            MiniRef = document.MiniRef,
            TextureMini = document.TextureMini,
            Wreck = document.Wreck
        };

        foreach (var alias in document.Aliases ?? Enumerable.Empty<VehicleAliasDocument>())
            texture.Aliases.Add(new VehicleAlias
            {
                Model = alias.Model,
                Group = alias.Group,
                MiniRef = alias.MiniRef,
                TextureMini = alias.TextureMini
            });

        if (document.Meta is not null)
        {
            texture.Meta = new VehicleMeta
            {
                Raw = document.Meta.Raw,
                Version = document.Meta.Version,
                Vehicle = document.Meta.Vehicle,
                Operator = document.Meta.Operator,
                Depot = document.Meta.Depot,
                RevisionDate = document.Meta.RevisionDate,
                RevisionPlace = document.Meta.RevisionPlace,
                TextureAuthor = document.Meta.TextureAuthor,
                PhotoAuthor = document.Meta.PhotoAuthor
            };

            if (document.Meta.Extra is not null)
                texture.Meta.Extra.AddRange(document.Meta.Extra);
        }

        return texture;
    }

    private static List<T>? OrNull<T>(List<T> items) => items.Count > 0 ? items : null;
}
