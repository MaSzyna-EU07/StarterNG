using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Vehicles;

namespace StarterNG.Infrastructure.Vehicles;

/// <summary>
/// Loads the catalogue from the starter's own JSON copy of the installation,
/// rebuilding that copy from textures.txt whenever the installation has moved on.
/// </summary>
/// <remarks>
/// The copy is a plain merged database, the same format a package is written in,
/// so it can be read by anything that reads the format - and it is a copy, never
/// a dependency: a missing, stale, unreadable or unwritable cache only costs the
/// parse it was meant to save. textures.txt stays the truth.
/// </remarks>
public sealed class VehicleCacheRepository : IVehicleRepository
{
    private const string DatabaseFileName = "vehicles.json";

    /// <summary>What the copy was built from, and what makes it stale.</summary>
    private const string SourcesFileName = "sources.txt";

    private readonly IFileSystem _files;
    private readonly IGamePaths _paths;
    private readonly IDiagnosticsLog _log;
    private readonly TexturesTxtVehicleRepository _legacy;
    private readonly VehicleJsonSerializer _serializer;
    private readonly string _directory;

    public VehicleCacheRepository(IFileSystem files, IGamePaths paths, IDiagnosticsLog log,
                                  TexturesTxtVehicleRepository legacy, VehicleJsonSerializer serializer,
                                  string directory)
    {
        _files = files;
        _paths = paths;
        _log = log;
        _legacy = legacy;
        _serializer = serializer;
        _directory = directory;
    }

    public int Load(VehicleCatalog catalog)
    {
        string stamp = Stamp();
        var entries = Cached(stamp) ?? Migrate(stamp);

        catalog.BeginLoad();

        int liveries = 0;
        foreach (var entry in entries)
        {
            liveries += entry.Textures.Count;
            catalog.Ingest(entry);
        }

        catalog.EndLoad();
        return liveries;
    }

    /// <summary>
    /// The installation as one comparable line per textures.txt. Every way the
    /// installation can move on - a file edited, added, removed, or the whole
    /// installation swapped for another one - shows up as a different text.
    /// </summary>
    private string Stamp()
    {
        var stamp = new StringBuilder().Append("root|").Append(Path.GetFullPath(_paths.Root)).Append('\n')
                                       .Append("parser|").Append(TexturesTxtParser.Version).Append('\n');

        foreach (string path in _legacy.Sources())
        {
            long ticks = 0;
            long size = 0;
            try
            {
                ticks = _files.GetLastWriteTimeUtc(path).Ticks;
                size = _files.GetFileSize(path);
            }
            catch (Exception ex)
            {
                _log.Log($"textures.txt {path}", ex);
            }

            stamp.Append(ticks).Append('|').Append(size).Append('|')
                 .Append(path.Replace('\\', '/')).Append('\n');
        }

        return stamp.ToString();
    }

    private List<VehicleEntry>? Cached(string stamp)
    {
        try
        {
            if (!_files.FileExists(SourcesPath) || !_files.FileExists(DatabasePath))
                return null;

            if (!string.Equals(_files.ReadAllText(SourcesPath), stamp, StringComparison.Ordinal))
                return null;

            var entries = _serializer.ReadCollection(_files.ReadAllText(DatabasePath));
            foreach (var entry in entries)
                if (!VehicleJsonSerializer.IsReadable(entry))
                {
                    _log.Log($"{DatabaseFileName}: kopia w wersji {entry.SchemaVersion}, " +
                             $"ten starter czyta do {VehicleJsonSerializer.SchemaVersion} - odtwarzam");
                    return null;
                }

            return entries;
        }
        catch (Exception ex)
        {
            _log.Log($"{DatabaseFileName}: kopia nie do odczytu, odtwarzam", ex);
            return null;
        }
    }

    private List<VehicleEntry> Migrate(string stamp)
    {
        var entries = new List<VehicleEntry>();
        foreach (string path in _legacy.Sources())
            if (_legacy.Read(path) is { } entry)
                entries.Add(entry);

        Store(entries, stamp);
        return entries;
    }

    /// <summary>
    /// Writes the copy out. Best effort by design - an installation on a read-only
    /// disk, or a home folder we cannot write, still gets its vehicles.
    /// </summary>
    private void Store(List<VehicleEntry> entries, string stamp)
    {
        try
        {
            _files.CreateDirectory(_directory);
            _files.WriteAllText(DatabasePath, _serializer.WriteCollection(entries));
            _files.WriteAllText(SourcesPath, stamp);
        }
        catch (Exception ex)
        {
            _log.Log($"{DatabaseFileName}: kopia nie do zapisania", ex);
        }
    }

    private string DatabasePath => Path.Combine(_directory, DatabaseFileName);

    private string SourcesPath => Path.Combine(_directory, SourcesFileName);
}
