using StarterNG.Infrastructure;
using StarterNG.Infrastructure.Adapters;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class InstallationCheckTests
{
    [Fact]
    public void An_empty_folder_reports_every_missing_game_directory()
    {
        var installation = new TestInstallation();
        var strings = installation.Strings.With("FaultNoDir", "missing {0}");
        var check = new InstallationCheck(installation.Paths, installation.Files, strings,
                                          installation.Physics, installation.Library, installation.SettingsStore);

        var faults = check.Run();

        Assert.Contains("missing /dynamic", faults);
        Assert.Contains("missing /scenery", faults);
        Assert.Contains("FaultNoWeights", faults);
    }

    [Fact]
    public void A_complete_installation_reports_no_missing_directories()
    {
        var files = new InMemoryFileSystem()
            .WithDirectory(Path.Combine("game", "dynamic"))
            .WithDirectory(Path.Combine("game", "sounds"))
            .WithDirectory(Path.Combine("game", "models"))
            .WithDirectory(Path.Combine("game", "scenery"))
            .WithDirectory(Path.Combine("game", "textures"))
            .WithFile(Path.Combine("game", "data", "load_weights.txt"), "");
        var installation = new TestInstallation(files);
        var strings = installation.Strings.With("FaultNoDir", "missing {0}");

        var faults = new InstallationCheck(installation.Paths, files, strings,
                                           installation.Physics, installation.Library, installation.SettingsStore).Run();

        Assert.DoesNotContain(faults, fault => fault.StartsWith("missing "));
        Assert.DoesNotContain("FaultNoWeights", faults);
    }

    [Fact]
    public void A_missing_simulator_is_asked_for_rather_than_called_a_broken_installation()
    {
        // The simulator may be a build kept elsewhere, or a Windows build for Wine.
        var installation = new TestInstallation();
        var check = Check(installation);

        Assert.DoesNotContain("FaultNoExe", check.Run());
        Assert.True(check.ExecutableMissing());
    }

    [Fact]
    public void A_simulator_in_the_installation_is_not_missing()
    {
        var installation = new TestInstallation(new InMemoryFileSystem().WithExecutable(TestInstallation.At("eu07")));

        Assert.False(Check(installation).ExecutableMissing());
    }

    [Fact]
    public void A_simulator_picked_from_elsewhere_is_not_missing()
    {
        string build = Path.Combine(Path.GetTempPath(), "maszyna", "bin", "eu07_2026-10-04_fc195880");
        var installation = new TestInstallation(new InMemoryFileSystem().WithExecutable(build));
        installation.SettingsStore.Settings.SelectExeAutomatically = false;
        installation.SettingsStore.Settings.ExecutablePath = build;

        Assert.False(Check(installation).ExecutableMissing());
    }

    private static InstallationCheck Check(TestInstallation installation) =>
        new(installation.Paths, installation.Files, installation.Strings, installation.Physics, installation.Library,
            installation.SettingsStore);
}
