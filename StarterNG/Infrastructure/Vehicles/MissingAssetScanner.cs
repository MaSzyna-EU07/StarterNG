using System.Collections.Generic;
using System.IO;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Vehicles;

namespace StarterNG.Infrastructure.Vehicles;

/// <summary>
/// Reports liveries whose skin or model file is not actually present, for the
/// optional "log missing vehicle files" diagnostic.
/// </summary>
/// <remarks>
/// Both assets are looked up under several extensions because the simulator
/// accepts a texture as .mat or .bmp and a model as .t3d or .e3d.
/// </remarks>
public sealed class MissingAssetScanner
{
    private static readonly string[] TextureExtensions = { ".mat", ".bmp" };
    private static readonly string[] ModelExtensions = { ".t3d", ".e3d" };

    private readonly IFileSystem _files;
    private readonly IGamePaths _paths;

    public MissingAssetScanner(IFileSystem files, IGamePaths paths)
    {
        _files = files;
        _paths = paths;
    }

    public List<string> Scan(VehicleCatalog catalog)
    {
        var lines = new List<string>();

        foreach (var texture in catalog.Textures)
        {
            string directory = Path.Combine(
                _paths.Dynamic,
                texture.Directory.Replace('/', Path.DirectorySeparatorChar).TrimEnd('\\', '/'));

            if (!string.IsNullOrEmpty(texture.Skinfile) &&
                !ExistsWithAnyExtension(Path.Combine(directory, texture.Skinfile), TextureExtensions))
                lines.Add($"# no file: {texture.Directory}{texture.Skinfile}");

            if (!string.IsNullOrEmpty(texture.Model) &&
                !ExistsWithAnyExtension(Path.Combine(directory, texture.Model), ModelExtensions))
                lines.Add($"# no model: {texture.Directory}{texture.Model}");
        }

        return lines;
    }

    private bool ExistsWithAnyExtension(string path, string[] extensions)
    {
        if (_files.FileExists(path))
            return true;

        foreach (string extension in extensions)
            if (_files.FileExists(Path.ChangeExtension(path, extension)))
                return true;

        return false;
    }
}
