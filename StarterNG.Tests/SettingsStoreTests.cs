using StarterNG.Domain.Settings;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class SettingsStoreTests
{
    private const string UserIni = "/home/kolejarz/.config/MaSzyna/eu07.ini";

    private static TestInstallation WithHome(InMemoryFileSystem? files = null)
    {
        var installation = new TestInstallation(files);
        installation.Environment.With("HOME", "/home/kolejarz");
        return installation;
    }

    [Fact]
    public void Settings_are_read_from_the_users_ini()
    {
        var installation = WithHome(new InMemoryFileSystem()
            .WithFile(UserIni, "width 1920\nheight 1080\nfullscreen yes\nlang pl\n"));

        installation.SettingsStore.Load();

        var settings = installation.SettingsStore.Settings;
        Assert.Equal(1920, settings.Width);
        Assert.Equal(1080, settings.Height);
        Assert.True(settings.Fullscreen);
        Assert.Equal("Polski", settings.Language);
        Assert.True(settings.LanguageWasSet);
        Assert.Equal(UserIni, installation.SettingsStore.LoadedFrom);
    }

    [Fact]
    public void A_first_run_falls_back_to_the_ini_shipped_with_the_installation()
    {
        var installation = WithHome(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("eu07.ini"), "width 800\n"));

        installation.SettingsStore.Load();

        Assert.Equal(800, installation.SettingsStore.Settings.Width);
        Assert.Equal(TestInstallation.At("eu07.ini"), installation.SettingsStore.LoadedFrom);
        // Saving still goes to the user's own file, never back into the installation.
        Assert.Equal(UserIni, installation.SettingsStore.SavePath);
    }

    [Fact]
    public void With_no_ini_at_all_the_defaults_stand()
    {
        var installation = WithHome();

        installation.SettingsStore.Load();

        Assert.Equal(1280, installation.SettingsStore.Settings.Width);
        Assert.False(installation.SettingsStore.Settings.LanguageWasSet);
    }

    [Fact]
    public void A_save_round_trips_the_settings()
    {
        var installation = WithHome();
        installation.SettingsStore.Load();
        installation.SettingsStore.Settings.Width = 2560;
        installation.SettingsStore.Settings.FieldOfView = 60;
        installation.SettingsStore.Save();

        var reopened = WithHome(installation.Files);
        reopened.SettingsStore.Load();

        Assert.Equal(2560, reopened.SettingsStore.Settings.Width);
        Assert.Equal(60, reopened.SettingsStore.Settings.FieldOfView);
    }

    [Fact]
    public void Keys_the_starter_does_not_manage_survive_a_round_trip()
    {
        var installation = WithHome(new InMemoryFileSystem()
            .WithFile(UserIni, "width 1280\nsomethingexotic 42\n"));
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        Assert.Contains("somethingexotic 42", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void A_registered_screen_is_asked_for_its_edits_before_a_save()
    {
        var installation = WithHome();
        installation.SettingsStore.Load();
        installation.SettingsStore.RegisterCapture(new WidthCapture(3840));

        installation.SettingsStore.CaptureAndSave();

        Assert.Equal(3840, installation.SettingsStore.Settings.Width);
    }

    [Fact]
    public void An_ini_written_by_someone_else_is_noticed()
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "width 1280\n"));
        installation.SettingsStore.Load();

        Assert.False(installation.SettingsStore.IsConfigNewerOnDisk());

        installation.Files.LastWriteTimeUtc = installation.Files.LastWriteTimeUtc.AddMinutes(5);

        Assert.True(installation.SettingsStore.IsConfigNewerOnDisk());
    }

    [Fact]
    public void On_windows_the_ini_lives_under_appdata()
    {
        var installation = new TestInstallation();
        installation.Environment.AsWindows();

        Assert.Equal(Path.Combine("C:/Users/kolejarz/AppData/Roaming", "MaSzyna", "eu07.ini"),
                     installation.SettingsPaths.UserConfigPath());
    }

    /// <summary>Stands in for the settings screen holding an unsaved edit.</summary>
    private sealed class WidthCapture : StarterNG.Application.ISettingsCapture
    {
        private readonly int _width;

        public WidthCapture(int width) => _width = width;

        public void CaptureInto(SimulatorSettings settings) => settings.Width = _width;
    }
}
