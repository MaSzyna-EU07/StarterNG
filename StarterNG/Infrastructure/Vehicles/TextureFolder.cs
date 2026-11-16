using System.IO;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Vehicles;

namespace StarterNG.Infrastructure.Vehicles;

public static class TextureFolder
{
    /// <summary>
    /// A livery's folder as it is named on disk: under dynamic, or from the game's folder
    /// when its name starts there, in any letter case. Where it would be when it is not there.
    /// </summary>
    public static string Of(IFileSystem files, IGamePaths paths, VehicleTexture texture)
    {
        string directory = texture.Directory.Replace('\\', '/').TrimEnd('/');
        if (directory.Length == 0)
            return paths.Dynamic;

        var names = new CaseInsensitivePaths(files);
        return names.Find(paths.Dynamic, directory, directory: true) ??
               names.Find(paths.Root, directory, directory: true) ??
               Path.Combine(paths.Root, directory.Replace('/', Path.DirectorySeparatorChar));
    }
}
