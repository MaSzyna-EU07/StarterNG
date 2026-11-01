using StarterNG.Application;
using StarterNG.Application.Abstractions;
using StarterNG.Classes;
using StarterNG.Infrastructure;
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
    public void With_the_physics_log_on_the_launch_is_noted_for_the_tapes()
    {
        var (start, launcher, installation) = Rig(files => files.WithExecutable(Build), sceneryClock: "07:52");
        installation.SettingsStore.Settings.PhysicsLog = true;

        start.Execute(freeFly: false, saveSettings: false);

        // Under the name the simulator is given, which is the name it logs under.
        string driven = launcher.Arguments![launcher.Arguments.ToList().IndexOf("-v") + 1];
        string noted = installation.Files.ReadAllText(TestInstallation.At("physicslog", "starter-launch.txt"));
        Assert.Contains("clock\t07:52", noted);
        Assert.Contains($"vehicle\t{driven}\tPKP/EP07\tep07.mmd", noted);
    }

    [Fact]
    public void Without_the_physics_log_nothing_is_noted()
    {
        var (start, _, installation) = Rig(files => files.WithExecutable(Build));

        start.Execute(freeFly: false, saveSettings: false);

        Assert.False(installation.Files.FileExists(TestInstallation.At("physicslog", "starter-launch.txt")));
    }

    [Theory]
    [InlineData("v2")]
    [InlineData("V2")]
    [InlineData("gone")]
    [InlineData(null)]
    [InlineData("")]
    public void A_crew_is_there_to_start_with_when_the_pick_has_one_or_names_nothing(string? preferred) =>
        Assert.True(StartSimulation.HasCrewToStart(WagonAndLocomotive, preferred));

    [Fact]
    public void A_pick_with_nobody_in_it_says_no_crew() =>
        Assert.False(StartSimulation.HasCrewToStart(WagonAndLocomotive, "v1"));

    [Fact]
    public void A_consist_nobody_sits_in_has_no_crew_to_start()
    {
        var empty = new Trainset("trainset a tor 0 0\nnode -1 0 v1 dynamic d s m 0 nobody 3 0 enddynamic\nendtrainset");

        Assert.False(StartSimulation.HasCrewToStart(empty, null));
    }

    private static Trainset WagonAndLocomotive => new(
        "trainset a tor 0 0\n" +
        "node -1 0 v1 dynamic d s m 0 nobody 3 0 enddynamic\n" +
        "node -1 0 v2 dynamic d s m 0 headdriver 3 0 enddynamic\n" +
        "endtrainset");

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
    public void Wine_is_pointed_at_the_settings_the_starter_saves()
    {
        string wine = Path.Combine("/usr/bin", "wine");
        string exe = Path.Combine(Path.GetTempPath(), "maszyna", "eu07.exe");
        const string roaming = "/home/kolejarz/.wine/drive_c/users/kolejarz/AppData/Roaming";
        var (start, _, installation) = Rig(files => files
            .WithExecutable(wine)
            .WithFile(exe, "MZ\u0090\u0000")
            .WithDirectory(roaming));
        installation.Environment.With("PATH", "/usr/bin").With("HOME", "/home/kolejarz").With("USER", "kolejarz");
        installation.SettingsStore.Settings.ExecutablePath = exe;

        start.Execute(freeFly: true, saveSettings: false);

        Assert.Equal(Path.GetFullPath(Path.Combine("/home/kolejarz", ".config", "MaSzyna")),
                     installation.Files.LinkTarget(Path.Combine(roaming, "MaSzyna")));
    }

    [Fact]
    public void A_native_build_leaves_the_wine_prefix_alone()
    {
        const string roaming = "/home/kolejarz/.wine/drive_c/users/kolejarz/AppData/Roaming";
        var (start, _, installation) = Rig(files => files.WithExecutable(Build).WithDirectory(roaming));
        installation.Environment.With("HOME", "/home/kolejarz").With("USER", "kolejarz");

        start.Execute(freeFly: true, saveSettings: false);

        Assert.Null(installation.Files.LinkTarget(roaming + "/MaSzyna"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Any_renderer_is_left_for_the_simulator_to_take(int renderer)
    {
        var (start, launcher, installation) = Rig(files => files.WithExecutable(Build, "\u007fELF binary"));
        installation.SettingsStore.Settings.RenderEngine = renderer;

        Assert.Equal(SimulationStartOutcome.Started, start.Execute(freeFly: true, saveSettings: false).Outcome);
        Assert.NotNull(launcher.Executable);
    }

    [Fact]
    public void The_error_stream_is_kept_only_while_the_starter_stays_to_read_it()
    {
        var (start, launcher, installation) = Rig(files => files.WithExecutable(Build));

        start.Execute(freeFly: true, saveSettings: false);
        Assert.True(launcher.CapturedErrors);

        // Closing right after the launch would leave the pipe with no reader.
        installation.SettingsStore.Settings.AutoCloseStarter = true;
        start.Execute(freeFly: true, saveSettings: false);
        Assert.False(launcher.CapturedErrors);
    }

    [Fact]
    public void The_editor_opens_the_scenery_itself_not_an_export()
    {
        var (start, launcher, installation) = Rig(files => files.WithExecutable(Build));

        var result = start.ExecuteEditor();

        Assert.Equal(SimulationStartOutcome.Started, result.Outcome);
        Assert.Equal(new[] { "-edit", "td.scn" }, launcher.Arguments);
        Assert.False(installation.Files.FileExists(TestInstallation.At("scenery", "$td.scn")));
    }

    [Fact]
    public void A_new_scenery_opens_at_the_wizard_even_with_a_scenery_selected()
    {
        var (start, launcher, _) = Rig(files => files.WithExecutable(Build));

        var result = start.ExecuteNewSceneryEditor();

        Assert.Equal(SimulationStartOutcome.Started, result.Outcome);
        Assert.Equal(new[] { "-edit" }, launcher.Arguments);
    }

    [Fact]
    public void With_no_scenery_selected_the_editor_opens_at_its_wizard()
    {
        var (start, launcher, _) = Rig(files => files.WithExecutable(Build), state => state.CurrentScenery = null);

        var result = start.ExecuteEditor();

        Assert.Equal(SimulationStartOutcome.Started, result.Outcome);
        Assert.Equal(new[] { "-edit" }, launcher.Arguments);
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

    [Fact]
    public void A_scenery_with_no_consist_picked_starts_with_the_free_camera()
    {
        var (start, launcher, _) = Rig(files => files.WithExecutable(Build), state => state.CurrentTrainset = null);

        var result = start.Execute(freeFly: false, saveSettings: false);

        Assert.Equal(SimulationStartOutcome.Started, result.Outcome);
        Assert.Equal(new[] { "-s", "$td.scn" }, launcher.Arguments);
    }

    [Fact]
    public void The_copy_for_a_start_is_named_in_lower_case_as_the_simulator_looks_for_it()
    {
        var (start, launcher, _) = Rig(files => files.WithExecutable(Build), sceneryFile: "Glinojeck.scn");

        start.Execute(freeFly: true, saveSettings: false);

        Assert.Equal(new[] { "-s", "$glinojeck.scn" }, launcher.Arguments);
    }

    [Fact]
    public void A_free_camera_start_without_a_consist_notes_no_driven_vehicle_for_the_tapes()
    {
        var (start, launcher, installation) = Rig(files => files.WithExecutable(Build),
                                                  state => state.CurrentTrainset = null);
        installation.SettingsStore.Settings.PhysicsLog = true;

        start.Execute(freeFly: true, saveSettings: false);

        Assert.NotNull(launcher.Executable);
        var note = StarterNG.Domain.Tapes.TapeLaunch.Parse(
            installation.Files.ReadAllText(TestInstallation.At("physicslog", "starter-launch.txt")))!;
        Assert.Null(note.Driven);
        Assert.Empty(note.Consist!);
    }

    [Fact]
    public void A_start_in_a_vehicle_notes_it_and_its_consist_for_the_tapes()
    {
        var (start, launcher, installation) = Rig(files => files.WithExecutable(Build));
        installation.SettingsStore.Settings.PhysicsLog = true;

        start.Execute(freeFly: false, saveSettings: false);

        // Under the name the simulator is given, which is the name it logs under.
        string given = launcher.Arguments![launcher.Arguments.ToList().IndexOf("-v") + 1];
        var note = StarterNG.Domain.Tapes.TapeLaunch.Parse(
            installation.Files.ReadAllText(TestInstallation.At("physicslog", "starter-launch.txt")))!;
        Assert.Equal(given, note.Driven);
        Assert.Equal(new[] { given }, note.Consist);
    }

    [Fact]
    public void A_start_notes_the_scenario_and_the_train_of_the_timetable_for_the_tapes()
    {
        var (start, _, installation) = Rig(files =>
        {
            files.WithExecutable(Build);
            files.WithFile(TestInstallation.At("scenery", "pociag.txt"),
                           "[ Rodzaj i numer pociagu   | MPJ15120 ]\r\n");
        });
        installation.SettingsStore.Settings.PhysicsLog = true;

        start.Execute(freeFly: false, saveSettings: false);

        var note = StarterNG.Domain.Tapes.TapeLaunch.Parse(
            installation.Files.ReadAllText(TestInstallation.At("physicslog", "starter-launch.txt")))!;
        Assert.Equal("Test", note.SceneryName);
        Assert.Equal("MPJ15120", note.Train);
    }

    [Fact]
    public void A_free_camera_start_notes_no_train_for_the_tapes()
    {
        var (start, _, installation) = Rig(files => files.WithExecutable(Build));
        installation.SettingsStore.Settings.PhysicsLog = true;

        start.Execute(freeFly: true, saveSettings: false);

        var note = StarterNG.Domain.Tapes.TapeLaunch.Parse(
            installation.Files.ReadAllText(TestInstallation.At("physicslog", "starter-launch.txt")))!;
        Assert.Null(note.Train);
    }

    private static void HybridLaptop(InMemoryFileSystem files)
    {
        files.WithFile("/sys/class/drm/card0/device/uevent",
                       "DRIVER=nvidia\nPCI_ID=10DE:2D19\nPCI_SLOT_NAME=0000:01:00.0\n")
             .WithFile("/sys/class/drm/card0/device/boot_vga", "0\n")
             .WithFile("/sys/class/drm/card1/device/uevent",
                       "DRIVER=amdgpu\nPCI_ID=1002:1900\nPCI_SLOT_NAME=0000:65:00.0\n")
             .WithFile("/sys/class/drm/card1/device/boot_vga", "1\n")
             .WithFile("/sys/class/drm/card1-eDP-1/status", "connected\n");
    }

    [Fact]
    public void On_a_laptop_with_two_cards_the_simulator_goes_to_the_discrete_one()
    {
        var (start, launcher, _) = Rig(files =>
        {
            files.WithExecutable(Build);
            HybridLaptop(files);
        });

        start.Execute(freeFly: true, saveSettings: false);

        Assert.Equal("1", launcher.Environment!["__NV_PRIME_RENDER_OFFLOAD"]);
    }

    [Fact]
    public void Left_to_the_system_the_simulator_gets_no_card_of_the_starters_choosing()
    {
        var (start, launcher, installation) = Rig(files =>
        {
            files.WithExecutable(Build);
            HybridLaptop(files);
        });
        installation.SettingsStore.Settings.Gpu = StarterNG.Domain.Settings.GpuPreference.System;

        start.Execute(freeFly: true, saveSettings: false);

        Assert.Empty(launcher.Environment!);
    }

    private static (StartSimulation Start, RecordingLauncher Launcher, TestInstallation Installation) Rig(
        Action<InMemoryFileSystem> addSimulator, Action<AppState>? arrange = null, string sceneryFile = "td.scn",
        string sceneryClock = "10:30")
    {
        var installation = new TestInstallation();
        addSimulator(installation.Files);

        var settings = installation.SettingsStore.Settings;
        settings.SelectExeAutomatically = false;
        settings.ExecutablePath = Build;

        var scenery = new SceneryParser(installation.Clock, installation.Random).Parse(
            TestInstallation.At("scenery", sceneryFile),
            """
            //$n Test
            trainset pociag tor_1 10 0
            node -1 -1 ep07-001 dynamic PKP/EP07 ep07 ep07.mmd 0 headdriver 0
            endtrainset
            FirstInit
            """);
        scenery.Weather.ScenarioTimeOverride = sceneryClock;
        var state = new AppState { CurrentScenery = scenery, CurrentTrainset = scenery.Trainsets[0] };
        arrange?.Invoke(state);

        var launcher = new RecordingLauncher();
        var start = new StartSimulation(state, installation.SettingsStore, installation.Library.Vehicles,
                                        installation.Executables,
                                        installation.Files, installation.Paths, launcher,
                                        installation.Random, installation.Log,
                                        new LinuxGpus(installation.Files, installation.Environment),
                                        installation.Environment,
                                        new WineConfigLink(installation.Files, installation.Environment,
                                                           installation.SettingsPaths, installation.Log),
                                        installation.Tapes);
        return (start, launcher, installation);
    }

    private sealed class RecordingLauncher : IProcessLauncher
    {
        public string? Executable { get; private set; }

        public string? WorkingDirectory { get; private set; }

        public IReadOnlyList<string>? Arguments { get; private set; }

        public bool? CapturedErrors { get; private set; }

        public IReadOnlyDictionary<string, string>? Environment { get; private set; }

        public IProcessHandle? Start(string executablePath, IReadOnlyList<string> arguments, string? workingDirectory,
                                     IReadOnlyDictionary<string, string> environment, bool captureErrors,
                                     out string? error)
        {
            Environment = environment;
            Executable = executablePath;
            CapturedErrors = captureErrors;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;
            error = null;
            return new RunningProcess();
        }

        public bool OpenInShell(string pathOrUrl) => false;

        public IProcessHandle? FindRunning(string processName) => null;

        public bool IsRunning(string processName) => false;
    }

    private sealed class RunningProcess : IProcessHandle
    {
        public bool HasExited => false;

        public int? ExitCode => null;

        public IReadOnlyList<string> ErrorTail => Array.Empty<string>();

        public Task WaitForExitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
