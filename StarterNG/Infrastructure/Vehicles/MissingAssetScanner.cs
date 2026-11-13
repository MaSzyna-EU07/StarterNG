using System.Collections.Generic;
using System.IO;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Vehicles;

namespace StarterNG.Infrastructure.Vehicles;

public sealed class MissingAssetScanner
{
    private static readonly string[] TextureExtensions = { ".mat", ".bmp" };
    private static readonly string[] ModelExtensions = { ".t3d", ".e3d" };

    private readonly IGamePaths _paths;
    private readonly CaseInsensitivePaths _names;

    public MissingAssetScanner(IFileSystem files, IGamePaths paths)
    {
        _paths = paths;
        _names = new CaseInsensitivePaths(files, keepListings: true);
    }

    public List<string> Scan(VehicleCatalog catalog)
    {
        var lines = new List<string>();
        try
        {
            foreach (var texture in catalog.Textures)
            {
                string directory = texture.Directory.Replace('\\', '/').TrimEnd('/');

                if (!string.IsNullOrEmpty(texture.Skinfile) &&
                    !ExistsWithAnyExtension(Under(directory, texture.Skinfile), TextureExtensions))
                    lines.Add($"# no file: {texture.Directory}{texture.Skinfile}");

                if (!string.IsNullOrEmpty(texture.Model) &&
                    !ExistsWithAnyExtension(Under(directory, texture.Model), ModelExtensions))
                    lines.Add($"# no model: {texture.Directory}{texture.Model}");
            }
        }
        finally
        {
            // The folders are listed once for the whole catalog, and not kept past it.
            _names.Forget();
        }

        return lines;
    }

    private static string Under(string directory, string name) =>
        directory.Length == 0 ? name : directory + "/" + name;

    /// <summary>Whether the file is under dynamic as named, or with one of the extensions, in any letter case.</summary>
    private bool ExistsWithAnyExtension(string name, string[] extensions)
    {
        if (_names.Resolve(_paths.Dynamic, name) is not null)
            return true;

        foreach (string extension in extensions)
            if (_names.Resolve(_paths.Dynamic, Path.ChangeExtension(name, extension)) is not null)
                return true;

        return false;
    }
}
