using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Vehicles;
using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Infrastructure.Vehicles;

/// <summary>
/// Loads the catalogue from the dynamic/&lt;maker&gt;/&lt;vehicle&gt;/textures.txt
/// files an installation ships.
/// </summary>
public sealed class TexturesTxtVehicleRepository : IVehicleRepository
{
    private const string TexturesFileName = "textures.txt";

    private readonly IFileSystem _files;
    private readonly IGamePaths _paths;
    private readonly IDiagnosticsLog _log;
    private readonly TexturesTxtParser _parser;
    private readonly Encoding _encoding;

    public TexturesTxtVehicleRepository(IFileSystem files, IGamePaths paths, IDiagnosticsLog log,
                                        TexturesTxtParser parser)
    {
        _files = files;
        _paths = paths;
        _log = log;
        _parser = parser;
        _encoding = LegacyText.CodePage1250;
    }

    public int Load(VehicleCatalog catalog)
    {
        catalog.BeginLoad();

        int liveries = 0;
        foreach (string file in TexturesFiles())
        {
            var entry = ReadEntry(file);
            if (entry is null)
                continue;

            liveries += entry.Textures.Count;
            catalog.Ingest(entry);
        }

        catalog.EndLoad();
        return liveries;
    }

    private VehicleEntry? ReadEntry(string path)
    {
        try
        {
            // Read as text so the code page survives: the credit lines carry
            // Polish characters that ReadAllLines' default encoding would mangle.
            string[] lines = _files.ReadAllText(path, _encoding).Split('\n');
            return _parser.Parse(RelativeDirectory(path), lines);
        }
        catch (Exception ex)
        {
            _log.Log($"textures.txt {path}", ex);
            return null;
        }
    }

    /// <summary>
    /// Rolling stock sits exactly two levels below dynamic/: a maker folder and a
    /// vehicle folder.
    /// </summary>
    private IEnumerable<string> TexturesFiles()
    {
        foreach (string maker in _files.GetDirectories(_paths.Dynamic))
        foreach (string vehicle in _files.GetDirectories(maker))
        {
            string path = Path.Combine(vehicle, TexturesFileName);
            if (_files.FileExists(path))
                yield return path;
        }
    }

    /// <summary>
    /// The folder as the scenery format spells it: relative to dynamic/, forward
    /// slashes, trailing slash.
    /// </summary>
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
