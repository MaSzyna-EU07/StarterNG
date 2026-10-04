using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StarterNG.Application.Abstractions;
using StarterNG.Classes;
using StarterNG.Domain;
using StarterNG.Domain.Settings;
using StarterNG.Domain.Vehicles;
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

    public StartSimulation(AppState state, SettingsStore settings, VehicleCatalog vehicles,
                           ExecutableLocator executables, IFileSystem files, IGamePaths paths,
                           IProcessLauncher processes, IRandomSource random, IDiagnosticsLog log)
    {
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
    /// Minidumps written since the simulator was started, newest first. The
    /// simulator names them crash_*.dmp relative to its working directory - the
    /// installation - and crashpad, where it is set up, keeps its own under
    /// crashdumps; the folder of the binary is looked at too, for a simulator that
    /// was once run from there.
    /// </summary>
    public IReadOnlyList<string> CrashDumpsSince(DateTime startedUtc, string executable)
    {
        var dumps = new List<string>();
        try
        {
            foreach (string directory in new[] { _paths.Root, Path.GetDirectoryName(executable) ?? _paths.Root }
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Where(_files.DirectoryExists))
                dumps.AddRange(_files.GetFiles(directory, "*.dmp"));

            string crashpad = _paths.FromRoot("crashdumps");
            if (_files.DirectoryExists(crashpad))
                dumps.AddRange(_files.GetFilesRecursive(crashpad, "*.dmp"));

            // A little slack: the start is taken just after the launch, not before it.
            var since = startedUtc - TimeSpan.FromSeconds(5);
            return dumps.Distinct(StringComparer.OrdinalIgnoreCase)
                        .Where(dump => _files.GetLastWriteTimeUtc(dump) >= since)
                        .OrderByDescending(_files.GetLastWriteTimeUtc)
                        .ToList();
        }
        catch (Exception ex)
        {
            _log.Log("looking for crash dumps", ex);
            return Array.Empty<string>();
        }
    }

    /// <summary>The end of the simulator's own log, for when it died without a word on its error stream.</summary>
    public IReadOnlyList<string> SimulatorLogTail(int count)
    {
        string path = _paths.FromRoot("log.txt");
        try
        {
            if (!_files.FileExists(path))
                return Array.Empty<string>();

            var lines = LegacyText.Decode(_files.ReadAllBytes(path))
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => line.Trim().Length > 0)
                .ToList();
            return lines.Skip(Math.Max(0, lines.Count - count)).ToList();
        }
        catch (Exception ex)
        {
            _log.Log($"reading {path}", ex);
            return Array.Empty<string>();
        }
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

        string? vehicle = TrainsetDisplay.UniquifyForLaunch(trainset, scenery, _state.StartingVehicleName, _vehicles)
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

        // A binary for the other system is let through: on Linux a Windows build
        // runs under Wine, started below, or Proton, and only the launch can tell.
        if (problem is not (ExeProblem.None or ExeProblem.WrongPlatform))
            return new SimulationStartResult(SimulationStartOutcome.ExecutableProblem, ExecutablePath: executable,
                                             Problem: problem);

        string[] arguments = freeFly || string.IsNullOrEmpty(vehicle)
            ? new[] { "-s", exportName }
            : new[] { "-s", exportName, "-v", vehicle };

        // The installation, not the folder of the binary: a simulator picked from a
        // build tree lives beside the data, and the data is what it opens.
        var (program, programArguments) = _executables.LaunchCommand(executable, arguments);
        // Its error stream is kept for a report on a crash - unless the starter closes
        // now, leaving nobody to read it or to report.
        bool watched = !_settings.Settings.AutoCloseStarter;
        var process = _processes.Start(program, programArguments, _paths.Root, watched, out string? error);
        if (process is null)
            return new SimulationStartResult(SimulationStartOutcome.LaunchFailed, ExecutablePath: executable,
                                             Detail: error);

        return new SimulationStartResult(SimulationStartOutcome.Started, process, executable);
    }

}
