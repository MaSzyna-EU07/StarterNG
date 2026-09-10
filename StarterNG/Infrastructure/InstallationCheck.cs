using System.Collections.Generic;
using System.IO;
using StarterNG.Application.Abstractions;
using StarterNG.Classes;
using StarterNG.Application;
using StarterNG.Domain.Settings;

namespace StarterNG.Infrastructure;

public sealed class InstallationCheck
{
    private static readonly string[] RequiredFolders = { "dynamic", "sounds", "models", "scenery", "textures" };

    private readonly IGamePaths _paths;
    private readonly IFileSystem _files;
    private readonly ILocalizedStrings _strings;
    private readonly IPhysicsRepository _physics;
    private readonly GameLibrary _library;
    private readonly SettingsStore _settings;

    public InstallationCheck(IGamePaths paths, IFileSystem files, ILocalizedStrings strings,
                             IPhysicsRepository physics, GameLibrary library, SettingsStore settings)
    {
        _paths = paths;
        _files = files;
        _strings = strings;
        _physics = physics;
        _library = library;
        _settings = settings;
    }

    public List<string> Run()
    {
        var faults = new List<string>();

        foreach (string folder in RequiredFolders)
            if (!_files.DirectoryExists(_paths.FromRoot(folder)))
                faults.Add(string.Format(_strings["FaultNoDir"], "/" + folder));

        if (!_files.FileExists(Path.Combine(_paths.Data, "load_weights.txt")))
            faults.Add(_strings["FaultNoWeights"]);

        if (_library.Sceneries.Count == 0)
            faults.Add(_strings["FaultNoScenery"]);

        if (_library.Vehicles.Textures.Count == 0)
            faults.Add(_strings["FaultNoVehicles"]);

        if (_physics.IndexedCount == 0)
            faults.Add(_strings["FaultNoPhysics"]);

        _settings.ResolveExecutable(out var problem);
        if (problem == ExeProblem.NotFound)
            faults.Add(_strings["FaultNoExe"]);

        return faults;
    }
}
