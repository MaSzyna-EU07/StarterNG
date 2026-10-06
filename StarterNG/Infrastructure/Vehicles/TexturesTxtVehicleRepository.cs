using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Vehicles;
using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Infrastructure.Vehicles;

public sealed class TexturesTxtVehicleRepository : IVehicleRepository
{
    private const string TexturesFileName = "textures.txt";

    private readonly IFileSystem _files;
    private readonly IGamePaths _paths;
    private readonly IDiagnosticsLog _log;
    private readonly TexturesTxtParser _parser;

    public TexturesTxtVehicleRepository(IFileSystem files, IGamePaths paths, IDiagnosticsLog log,
                                        TexturesTxtParser parser)
    {
        _files = files;
        _paths = paths;
        _log = log;
        _parser = parser;
    }

    public int Load(VehicleCatalog catalog)
    {
        catalog.BeginLoad();

        int liveries = 0;
        foreach (string file in Sources())
        {
            var entry = Read(file);
            if (entry is null)
                continue;

            liveries += entry.Textures.Count;
            catalog.Ingest(entry);
        }

        catalog.EndLoad();
        return liveries;
    }

    /// <summary>
    /// Every textures.txt of the installation, in name order. Sorted rather than
    /// taken as the file system hands them over, so that which of two folders
    /// claiming the same skin wins is the same on every machine.
    /// </summary>
    public IEnumerable<string> Sources()
    {
        foreach (string maker in Sorted(_files.GetDirectories(_paths.Dynamic)))
        foreach (string vehicle in Sorted(_files.GetDirectories(maker)))
        {
            string path = Path.Combine(vehicle, TexturesFileName);
            if (_files.FileExists(path))
                yield return path;
        }
    }

    private static IEnumerable<string> Sorted(IEnumerable<string> directories) =>
        directories.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads one textures.txt; a file that will not parse is logged and skipped.</summary>
    public VehicleEntry? Read(string path)
    {
        try
        {
            string[] lines = LegacyText.Decode(_files.ReadAllBytes(path)).Split('\n');
            return _parser.Parse(RelativeDirectory(path), lines);
        }
        catch (Exception ex)
        {
            _log.Log($"textures.txt {path}", ex);
            return null;
        }
    }

    private string RelativeDirectory(string texturesPath)
    {
        string directory = Path.GetDirectoryName(texturesPath) ?? "";
        string full = Path.GetFullPath(directory);
        string root = Path.GetFullPath(_paths.Dynamic);

        string relative = full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? full[root.Length..].TrimStart(Path.DirectorySeparatorChar, '/')
            : directory;

        return relative.Replace('\\', '/').TrimEnd('/') + "/";
    }
}
