using System;
using System.IO;
using System.Linq;
using System.Text;
using StarterNG.Application.Abstractions;
using StarterNG.Classes;
using StarterNG.Domain;
using StarterNG.Domain.Settings;

namespace StarterNG.Application;

/// <summary>Why a start attempt ended the way it did.</summary>
public enum SimulationStartOutcome
{
    Started,

    /// <summary>Nothing is selected to run.</summary>
    NothingSelected,

    /// <summary>The scenario could not be written for the simulator to read.</summary>
    ExportFailed,

    /// <summary>The executable is missing, not executable, or built for another platform.</summary>
    ExecutableProblem,

    /// <summary>The executable is fine but the process would not start.</summary>
    LaunchFailed
}

/// <summary>
/// What happened, and what the caller needs in order to say so or to watch the
/// simulator it just started.
/// </summary>
public readonly record struct SimulationStartResult(
    SimulationStartOutcome Outcome,
    IProcessHandle? Process = null,
    string ExecutablePath = "",
    ExeProblem Problem = ExeProblem.None,
    string? Detail = null);

/// <summary>
/// Starts the simulator on the scenario and consist the user has selected.
/// </summary>
/// <remarks>
/// This was the middle of a 100-line click handler that also drove dialogs and
/// the loading screen. The sequence itself - settle the consist's starting
/// state, write the scenario the simulator will read, save the settings, launch -
/// is application logic and belongs away from the window; what is left in the
/// view is asking the user things and showing what came back.
/// </remarks>
public sealed class StartSimulation
{
    /// <summary>
    /// A scenario the starter has rewritten is saved beside the original under a
    /// '$' name, which the simulator loads and the scenery list ignores.
    /// </summary>
    private const char ExportPrefix = '$';

    /// <summary>Battery voltage stood in for by a token velocity, as the format demands.</summary>
    private const float BatteryOnVelocity = 0.1f;

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

    /// <summary>
    /// The vehicle the player would start in, or null when the consist has no one
    /// aboard - which is what the caller warns about before committing.
    /// </summary>
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

    /// <param name="freeFly">Start as a free camera rather than in a vehicle.</param>
    /// <param name="saveSettings">Write the settings before launching.</param>
    public SimulationStartResult Execute(bool freeFly, bool saveSettings)
    {
        var scenery = _state.CurrentScenery;
        var trainset = _state.CurrentTrainset;
        if (scenery is null || trainset is null)
            return new SimulationStartResult(SimulationStartOutcome.NothingSelected);

        ApplyBatteryDefault(trainset);

        string? vehicle = TrainsetDisplay.UniquifyForLaunch(trainset, scenery, _state.StartingVehicleName)
                          ?? StartableVehicle(trainset, _state.StartingVehicleName);
        _state.StartingVehicleName = vehicle;

        // The export always rewrites the weather block, so a run reflects what the
        // weather tab shows even when the user never touched it.
        scenery.Weather.Dirty = true;

        string exportName = ExportPrefix + Path.GetFileName(scenery.Path);
        string exportPath = Path.Combine(Path.GetDirectoryName(scenery.Path) ?? "scenery", exportName);
        try
        {
            _files.WriteAllText(exportPath,
                                scenery.BuildExportContent(_settings.Settings.IgnoreIrrelevantTrains, _random),
                                LegacyEncoding());
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

    /// <summary>
    /// Applies the user's battery preference to the consist. The scenery format
    /// carries the battery state in the trainset's velocity field, so "always on"
    /// means a token non-zero value when the consist is standing still.
    /// </summary>
    private void ApplyBatteryDefault(Trainset trainset)
    {
        if (trainset.Vehicles.Count == 0)
            return;

        trainset.Velocity = _settings.Settings.BatteryDefault switch
        {
            BatteryDefault.AlwaysOff => 0f,
            BatteryDefault.AlwaysOn => MathF.Abs(trainset.OriginalVelocity) < 0.01f
                ? BatteryOnVelocity
                : trainset.OriginalVelocity,
            _ => trainset.OriginalVelocity
        };
    }

    private static Encoding LegacyEncoding()
    {
        try
        {
            return Encoding.GetEncoding(1250);
        }
        catch (Exception)
        {
            return Encoding.Latin1;
        }
    }
}
