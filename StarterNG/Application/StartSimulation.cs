using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StarterNG.Application.Abstractions;
using StarterNG.Classes;
using StarterNG.Domain;
using StarterNG.Domain.Settings;
using StarterNG.Domain.Vehicles;
using StarterNG.Infrastructure;
using StarterNG.Infrastructure.Adapters;
using StarterNG.Infrastructure.Settings;

namespace StarterNG.Application;

public enum SimulationStartOutcome
{
    Started,

    NothingSelected,

    ExportFailed,

    ExecutableProblem,

    LaunchFailed
}

public readonly record struct SimulationStartResult(
    SimulationStartOutcome Outcome,
    IProcessHandle? Process = null,
    string ExecutablePath = "",
    ExeProblem Problem = ExeProblem.None,
    string? Detail = null);

public sealed class StartSimulation
{
    private const char ExportPrefix = '$';


    private readonly AppState _state;
    private readonly SettingsStore _settings;
    private readonly VehicleCatalog _vehicles;
    private readonly ExecutableLocator _executables;
    private readonly IFileSystem _files;
    private readonly IGamePaths _paths;
    private readonly IProcessLauncher _processes;
    private readonly IRandomSource _random;
    private readonly IDiagnosticsLog _log;
    private readonly LinuxGpus _gpus;
    private readonly IEnvironment _environment;
    private readonly WineConfigLink _wineConfig;
    private readonly HaslerTapes _tapes;

    public StartSimulation(AppState state, SettingsStore settings, VehicleCatalog vehicles,
                           ExecutableLocator executables, IFileSystem files, IGamePaths paths,
                           IProcessLauncher processes, IRandomSource random, IDiagnosticsLog log,
                           LinuxGpus gpus, IEnvironment environment, WineConfigLink wineConfig,
                           HaslerTapes tapes)
    {
        _gpus = gpus;
        _wineConfig = wineConfig;
        _environment = environment;
        _tapes = tapes;
        _state = state;
        _settings = settings;
        _vehicles = vehicles;
        _executables = executables;
        _files = files;
        _paths = paths;
        _processes = processes;
        _random = random;
        _log = log;
    }

    /// <summary>
    /// What the start button says of the consist. The vehicle picked answers for it
    /// even with nobody in it, where the launch itself goes on to the first crew.
    /// </summary>
    public static bool HasCrewToStart(Trainset trainset, string? preferred)
    {
        static bool CanStart(Dynamic vehicle) =>
            vehicle.DriverType is eDriverType.Headdriver or eDriverType.Reardriver or eDriverType.Passenger;

        if (!string.IsNullOrEmpty(preferred))
        {
            var picked = trainset.Vehicles.FirstOrDefault(vehicle =>
                string.Equals(vehicle.Name, preferred, StringComparison.OrdinalIgnoreCase));
            if (picked is not null)
                return CanStart(picked);
        }

        return trainset.Vehicles.Any(CanStart);
    }

    public SimulationStartResult Execute(bool freeFly, bool saveSettings)
    {
        var scenery = _state.CurrentScenery;
        var trainset = _state.CurrentTrainset;
        if (scenery is null)
            return new SimulationStartResult(SimulationStartOutcome.NothingSelected);

        // A scenery with no consist picked starts with the free camera.
        string? vehicle = null;
        if (trainset is null)
        {
            freeFly = true;
        }
        else
        {
            // The depot checkbox is the consist's own state; this setting is a blanket
            // override for people who always want the same thing, and it wins.
            switch (_settings.Settings.ReadyOverride)
            {
                case ConsistReadyOverride.AlwaysCold:
                    trainset.ReadyToGo = false;
                    break;
                case ConsistReadyOverride.AlwaysReady:
                    trainset.ReadyToGo = true;
                    break;
            }

            vehicle = TrainsetDisplay.UniquifyForLaunch(trainset, scenery, _state.StartingVehicleName, _vehicles);
            _state.StartingVehicleName = vehicle;
        }

        scenery.Weather.Dirty = true;

        // In lower case: that is the name the simulator looks for, and on Linux
        // "$Glinojeck.scn" is not "$glinojeck.scn".
        string exportName = ExportPrefix + SimulatorNames.Lower(Path.GetFileName(scenery.Path));
        string exportPath = Path.Combine(Path.GetDirectoryName(scenery.Path) ?? "scenery", exportName);
        try
        {
            _files.WriteAllText(exportPath,
                                scenery.BuildExportContent(_settings.Settings.IgnoreIrrelevantTrains, _random),
                                LegacyText.CodePage1250);
        }
        catch (Exception ex)
        {
            _log.Log($"writing {exportPath}", ex);
            return new SimulationStartResult(SimulationStartOutcome.ExportFailed, Detail: ex.Message,
                                             ExecutablePath: exportPath);
        }

        if (saveSettings)
            _settings.CaptureAndSave();

        if (_settings.Settings.PhysicsLog)
            _tapes.RecordLaunch(scenery, trainset, freeFly ? null : vehicle);

        string[] arguments = freeFly || string.IsNullOrEmpty(vehicle)
            ? new[] { "-s", exportName }
            : new[] { "-s", exportName, "-v", vehicle };

        return Launch(arguments);
    }

    /// <summary>
    /// Opens the selected scenery in the simulator's editor. The scenery file itself,
    /// not an export: what the editor saves has to land in the scenery. With none
    /// selected the simulator opens its wizard of a new scenery.
    /// </summary>
    public SimulationStartResult ExecuteEditor() =>
        Launch(_state.CurrentScenery is { } scenery
            ? new[] { "-edit", Path.GetFileName(scenery.Path) }
            : new[] { "-edit" });

    /// <summary>The simulator's wizard of a new scenery, whichever one is selected.</summary>
    public SimulationStartResult ExecuteNewSceneryEditor() => Launch(new[] { "-edit" });

    private SimulationStartResult Launch(IReadOnlyList<string> arguments)
    {
        string executable = Path.GetFullPath(_settings.ResolveExecutable(out var problem));

        // A binary for the other system is let through: on Linux a Windows build
        // runs under Wine, started below, or Proton, and only the launch can tell.
        if (problem is not (ExeProblem.None or ExeProblem.WrongPlatform))
            return new SimulationStartResult(SimulationStartOutcome.ExecutableProblem, ExecutablePath: executable,
                                             Problem: problem);

        // The installation, not the folder of the binary: a simulator picked from a
        // build tree lives beside the data, and the data is what it opens.
        var (program, programArguments) = _executables.LaunchCommand(executable, arguments);
        // Under Wine eu07.exe would read the prefix's own eu07.ini, not the one just saved.
        if (_executables.RunsThroughWine(executable))
            _wineConfig.Ensure();
        // Its error stream is kept for a report on a crash - unless the starter closes
        // now, leaving nobody to read it or to report.
        bool watched = !_settings.Settings.AutoCloseStarter;
        var process = _processes.Start(program, programArguments, _paths.Root, GraphicsCard(), watched,
                                       out string? error);
        if (process is null)
            return new SimulationStartResult(SimulationStartOutcome.LaunchFailed, ExecutablePath: executable,
                                             Detail: error);

        return new SimulationStartResult(SimulationStartOutcome.Started, process, executable);
    }

    /// <summary>
    /// What puts the simulator on the card the settings ask for, said in the log
    /// either way: a slow simulator on a laptop is the first thing to ask about.
    /// </summary>
    private IReadOnlyDictionary<string, string> GraphicsCard()
    {
        var gpus = _gpus.List();
        if (gpus.Count < 2)
            return new Dictionary<string, string>();

        var preference = _settings.Settings.Gpu;
        string cards = string.Join(", ", gpus);
        if (GpuOffload.Target(gpus, preference, _environment.GetVariable) is not { } target)
        {
            _log.Log($"GPU: {cards}; left to the system ({(preference == GpuPreference.System
                ? "chosen in the settings"
                : "set by the player, or no card to move to")})");
            return new Dictionary<string, string>();
        }

        var environment = GpuOffload.EnvironmentFor(target);
        _log.Log($"GPU: {cards}; simulator on {target} with " +
                 string.Join(' ', environment.Select(pair => $"{pair.Key}={pair.Value}")));
        return environment;
    }

}
