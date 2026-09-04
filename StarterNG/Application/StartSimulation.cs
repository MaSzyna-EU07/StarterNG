using System;
using System.IO;
using System.Linq;
using System.Text;
using StarterNG.Application.Abstractions;
using StarterNG.Classes;
using StarterNG.Domain;
using StarterNG.Domain.Settings;
using StarterNG.Infrastructure.Adapters;

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
    private readonly IFileSystem _files;
    private readonly IProcessLauncher _processes;
    private readonly IRandomSource _random;
    private readonly IDiagnosticsLog _log;

    public StartSimulation(AppState state, SettingsStore settings, IFileSystem files, IProcessLauncher processes,
                           IRandomSource random, IDiagnosticsLog log)
    {
        _state = state;
        _settings = settings;
        _files = files;
        _processes = processes;
        _random = random;
        _log = log;
    }

    public static string? StartableVehicle(Trainset? trainset, string? preferred)
    {
        if (trainset is null || trainset.Vehicles.Count == 0)
            return null;

        static bool CanStart(Dynamic vehicle) =>
            vehicle.DriverType is eDriverType.Headdriver or eDriverType.Reardriver or eDriverType.Passenger;

        if (!string.IsNullOrEmpty(preferred))
        {
            var picked = trainset.Vehicles.FirstOrDefault(vehicle =>
                string.Equals(vehicle.Name, preferred, StringComparison.OrdinalIgnoreCase));
            if (picked is not null && CanStart(picked))
                return picked.Name;
        }

        return trainset.Vehicles.FirstOrDefault(CanStart)?.Name;
    }

    public SimulationStartResult Execute(bool freeFly, bool saveSettings)
    {
        var scenery = _state.CurrentScenery;
        var trainset = _state.CurrentTrainset;
        if (scenery is null || trainset is null)
            return new SimulationStartResult(SimulationStartOutcome.NothingSelected);

        string? vehicle = TrainsetDisplay.UniquifyForLaunch(trainset, scenery, _state.StartingVehicleName)
                          ?? StartableVehicle(trainset, _state.StartingVehicleName);
        _state.StartingVehicleName = vehicle;

        scenery.Weather.Dirty = true;

        string exportName = ExportPrefix + Path.GetFileName(scenery.Path);
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

        string executable = Path.GetFullPath(_settings.ResolveExecutable(out var problem));
        if (problem != ExeProblem.None)
            return new SimulationStartResult(SimulationStartOutcome.ExecutableProblem, ExecutablePath: executable,
                                             Problem: problem);

        string[] arguments = freeFly || string.IsNullOrEmpty(vehicle)
            ? new[] { "-s", exportName }
            : new[] { "-s", exportName, "-v", vehicle };

        var process = _processes.Start(executable, arguments, Path.GetDirectoryName(executable), out string? error);
        if (process is null)
            return new SimulationStartResult(SimulationStartOutcome.LaunchFailed, ExecutablePath: executable,
                                             Detail: error);

        return new SimulationStartResult(SimulationStartOutcome.Started, process, executable);
    }

}
