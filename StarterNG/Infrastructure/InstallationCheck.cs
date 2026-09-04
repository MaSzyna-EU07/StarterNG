using System.Collections.Generic;
using System.IO;
using StarterNG.Application.Abstractions;
using StarterNG.Classes;

namespace StarterNG.Infrastructure;

/// <summary>
/// Verifies that the folder the starter runs from actually looks like a MaSzyna
/// installation, and reports what is missing in the user's language.
/// </summary>
/// <remarks>
/// Depends only on ports, so a broken installation can be described in a test by
/// handing it an empty in-memory file system.
/// </remarks>
public sealed class InstallationCheck
{
    private static readonly string[] RequiredFolders = { "dynamic", "sounds", "models", "scenery", "textures" };

    private readonly IGamePaths _paths;
    private readonly IFileSystem _files;
    private readonly ILocalizedStrings _strings;

    public InstallationCheck(IGamePaths paths, IFileSystem files, ILocalizedStrings strings)
    {
        _paths = paths;
        _files = files;
        _strings = strings;
    }

    public List<string> Run()
    {
        var faults = new List<string>();

        foreach (string folder in RequiredFolders)
            if (!_files.DirectoryExists(_paths.FromRoot(folder)))
                faults.Add(string.Format(_strings["FaultNoDir"], "/" + folder));

        if (!_files.FileExists(Path.Combine(_paths.Data, "load_weights.txt")))
            faults.Add(_strings["FaultNoWeights"]);

        if (GameData.Instance.Sceneries.Count == 0)
            faults.Add(_strings["FaultNoScenery"]);

        if (GameData.Instance.Vehicles.Textures.Count == 0)
            faults.Add(_strings["FaultNoVehicles"]);

        if (Physics.IndexedCount == 0)
            faults.Add(_strings["FaultNoPhysics"]);

        if (Settings.Instance.ResolveExecutable(out var problem) is var _ && problem == ExeProblem.NotFound)
            faults.Add(_strings["FaultNoExe"]);

        return faults;
    }
}
