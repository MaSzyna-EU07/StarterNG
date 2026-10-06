using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Vehicles;

namespace StarterNG.Infrastructure.Vehicles;

/// <summary>
/// Reads the vehicle catalogue from databases/vehicles: a merged vehicles.json
/// holding the whole database, and one file per package layered on top of it.
/// </summary>
public sealed class VehicleJsonRepository : IVehicleRepository
{
    private const string MergedFileName = "vehicles.json";

    private readonly IFileSystem _files;
    private readonly IGamePaths _paths;
    private readonly IDiagnosticsLog _log;
    private readonly VehicleJsonSerializer _serializer;

    public VehicleJsonRepository(IFileSystem files, IGamePaths paths, IDiagnosticsLog log,
                                 VehicleJsonSerializer serializer)
    {
        _files = files;
        _paths = paths;
        _log = log;
        _serializer = serializer;
    }

    public int Load(VehicleCatalog catalog)
    {
        catalog.BeginLoad();

        int liveries = 0;
        foreach (string file in DatabaseFiles())
            liveries += ReadInto(catalog, file);

        catalog.EndLoad();
        return liveries;
    }

    private int ReadInto(VehicleCatalog catalog, string path)
    {
        try
        {
            string json = _files.ReadAllText(path);
            var entries = IsMerged(path)
                ? _serializer.ReadCollection(json)
                : Listed(_serializer.Read(json));

            int liveries = 0;
            foreach (var entry in entries)
            {
                if (!VehicleJsonSerializer.IsReadable(entry))
                {
                    _log.Log($"{Name(path)}: format w wersji {entry.SchemaVersion}, " +
                             $"ten starter czyta do {VehicleJsonSerializer.SchemaVersion}");
                    continue;
                }

                liveries += entry.Textures.Count;
                catalog.Ingest(entry);
            }

            return liveries;
        }
        catch (Exception ex)
        {
            _log.Log(Name(path), ex);
            return 0;
        }
    }

    /// <summary>
    /// The merged database first and the packages after it, both in a fixed order,
    /// so which entry wins a clash is the same on every machine - the file system
    /// does not promise one.
    /// </summary>
    private IEnumerable<string> DatabaseFiles()
    {
        var files = _files.GetFiles(_paths.VehicleDatabase, "*.json")
                          .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                          .ToList();

        foreach (string file in files.Where(IsMerged))
            yield return file;

        foreach (string file in files.Where(file => !IsMerged(file)))
            yield return file;
    }

    private static bool IsMerged(string path) =>
        string.Equals(Path.GetFileName(path), MergedFileName, StringComparison.OrdinalIgnoreCase);

    private static string Name(string path) => $"databases/vehicles/{Path.GetFileName(path)}";

    private static List<VehicleEntry> Listed(VehicleEntry? entry) =>
        entry is null ? new List<VehicleEntry>() : new List<VehicleEntry> { entry };
}
