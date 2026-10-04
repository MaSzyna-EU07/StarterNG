using StarterNG.Application;
using StarterNG.Application.Abstractions;
using StarterNG.Infrastructure.Sceneries;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class StartSimulationTests
{
    // A simulator built from source: its own tree, away from the game data.
    private static readonly string Build =
        Path.Combine(Path.GetTempPath(), "maszyna", "cmake-build-diag", "bin", "eu07_2026-10-04_fc195880");

    [Fact]
    public void A_simulator_from_a_build_tree_runs_in_the_installation()
    {
        var (start, launcher, installation) = Rig(files => files.WithExecutable(Build));

        var result = start.Execute(freeFly: true, saveSettings: false);

        Assert.Equal(SimulationStartOutcome.Started, result.Outcome);
        Assert.Equal(Path.GetFullPath(Build), launcher.Executable);
        Assert.Equal(installation.Paths.Root, launcher.WorkingDirectory);
    }

    [Fact]
    public void A_build_for_the_other_system_is_still_launched()
    {
        // A Windows build on Linux: Wine or Proton may well run it.
        var (start, launcher, _) = Rig(files => files.WithExecutable(Build, "MZ\u0090\u0000"));

        Assert.Equal(SimulationStartOutcome.Started, start.Execute(freeFly: true, saveSettings: false).Outcome);
        Assert.NotNull(launcher.Executable);
    }

    [Fact]
    public void A_windows_build_on_linux_is_started_through_wine()
    {
        string wine = Path.Combine("/usr/bin", "wine");
        string exe = Path.Combine(Path.GetTempPath(), "maszyna", "eu07.exe");
        var (start, launcher, installation) = Rig(files => files
            .WithExecutable(wine)
            .WithFile(exe, "MZ\u0090\u0000"));
        installation.Environment.With("PATH", "/usr/bin");
        installation.SettingsStore.Settings.ExecutablePath = exe;

        var result = start.Execute(freeFly: true, saveSettings: false);

        // Unpacked from the Windows zip, it has no execute bit - Wine does not need one.
        Assert.Equal(SimulationStartOutcome.Started, result.Outcome);
        Assert.Equal(wine, launcher.Executable);
        Assert.Equal(exe, launcher.Arguments![0]);
        Assert.Equal("-s", launcher.Arguments[1]);
        Assert.Equal(installation.Paths.Root, launcher.WorkingDirectory);
    }

    [Fact]
    public void A_file_that_cannot_be_run_is_not()
    {
        var (start, launcher, _) = Rig(files => files.WithFile(Build, "\u007fELF"));

        var result = start.Execute(freeFly: true, saveSettings: false);

        Assert.Equal(SimulationStartOutcome.ExecutableProblem, result.Outcome);
        Assert.Equal(Domain.Settings.ExeProblem.NotExecutable, result.Problem);
        Assert.Null(launcher.Executable);
    }

    private static (StartSimulation Start, RecordingLauncher Launcher, TestInstallation Installation) Rig(
        Action<InMemoryFileSystem> addSimulator)
    {
        var installation = new TestInstallation();
        addSimulator(installation.Files);

        var settings = installation.SettingsStore.Settings;
        settings.SelectExeAutomatically = false;
        settings.ExecutablePath = Build;

        var scenery = new SceneryParser(installation.Clock, installation.Random).Parse(
            TestInstallation.At("scenery", "td.scn"),
            """
            //$n Test
            trainset pociag tor_1 10 0
            node -1 -1 ep07-001 dynamic PKP/EP07 ep07 ep07.mmd 0 headdriver 0
            endtrainset
            FirstInit
            """);
        var state = new AppState { CurrentScenery = scenery, CurrentTrainset = scenery.Trainsets[0] };

        var launcher = new RecordingLauncher();
        var start = new StartSimulation(state, installation.SettingsStore, installation.Library.Vehicles,
                                        installation.Executables, installation.Files, installation.Paths, launcher,
                                        installation.Random, installation.Log);
        return (start, launcher, installation);
    }

    private sealed class RecordingLauncher : IProcessLauncher
    {
        public string? Executable { get; private set; }

        public string? WorkingDirectory { get; private set; }

        public IReadOnlyList<string>? Arguments { get; private set; }

        public IProcessHandle? Start(string executablePath, IReadOnlyList<string> arguments, string? workingDirectory,
                                     out string? error)
        {
            Executable = executablePath;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;
            error = null;
            return new RunningProcess();
        }

        public bool OpenInShell(string pathOrUrl) => false;

        public IProcessHandle? FindRunning(string processName) => null;
    }

    private sealed class RunningProcess : IProcessHandle
    {
        public bool HasExited => false;

        public Task WaitForExitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
