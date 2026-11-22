using StarterNG.Infrastructure;

namespace StarterNG.Tests;

public class MonitorsTests
{
    [Fact]
    public void A_monitor_is_named_as_the_simulator_names_it()
    {
        Assert.Equal("Generic_PnP_Monitor:1920,0", Monitors.IdOf("Generic PnP Monitor", 1920, 0));
        Assert.Equal("HDMI-1:-1280,-200", Monitors.IdOf("HDMI-1", -1280, -200));
    }

    [Fact]
    public void Xrandr_lists_the_connected_outputs_with_their_place()
    {
        const string report =
            "Screen 0: minimum 320 x 200, current 4480 x 1440, maximum 16384 x 16384\n" +
            "eDP-1 connected primary 1920x1080+0+360 (normal left inverted right x axis y axis) 344mm x 194mm\n" +
            "   1920x1080     60.00*+\n" +
            "HDMI-1 connected 2560x1440+1920+0 (normal left inverted right x axis y axis) 597mm x 336mm\n" +
            "DP-1 disconnected (normal left inverted right x axis y axis)\n" +
            "DP-2 connected (normal left inverted right x axis y axis)\n";

        var monitors = Monitors.Parse(report);

        Assert.Equal(2, monitors.Count);
        Assert.Equal(new DisplayMonitor("eDP-1:0,360", "eDP-1", 1920, 1080, true, 60), monitors[0]);
        Assert.Equal(new DisplayMonitor("HDMI-1:1920,0", "HDMI-1", 2560, 1440, false), monitors[1]);
    }

    [Fact]
    public void Xrandr_gives_each_monitor_the_rate_its_mode_runs_at()
    {
        const string report =
            "DP-1 connected primary 2560x1440+0+0 (normal left inverted right x axis y axis) 597mm x 336mm\n" +
            "   2560x1440     59.95 +  165.08*  143.97\n" +
            "   1920x1080     60.00    50.00\n" +
            "HDMI-1 connected 1920x1080+2560+0 (normal left inverted right x axis y axis) 527mm x 296mm\n" +
            "   1920x1080     60.00*+  50.00\n" +
            "DP-2 disconnected (normal left inverted right x axis y axis)\n" +
            "   1280x1024     75.02*\n";

        var monitors = Monitors.Parse(report);

        Assert.Equal(new[] { 165, 60 }, monitors.Select(m => m.RefreshRate));
    }

    [Fact]
    public void The_monitor_is_written_only_when_one_is_chosen()
    {
        var s = new StarterNG.Domain.Settings.SimulatorSettings();
        var c = StarterNG.Classes.ConfigFile.Parse("");
        new StarterNG.Infrastructure.Settings.SettingsSerializer().Write(s, c);
        Assert.False(c.Has("fullscreenmonitor"));

        s.FullscreenMonitor = "HDMI-1:1920,0";
        new StarterNG.Infrastructure.Settings.SettingsSerializer().Write(s, c);
        Assert.Equal("HDMI-1:1920,0", c.GetRaw("fullscreenmonitor"));
    }

    // ── A name outside ASCII: the simulator compares the file's bytes with GLFW's UTF-8 name ──

    private static readonly string UserIni = Path.Combine("/home/kolejarz", ".config", "MaSzyna", "eu07.ini");

    private static StarterNG.Tests.Fakes.TestInstallation WithIni(byte[] ini)
    {
        var files = new StarterNG.Tests.Fakes.InMemoryFileSystem();
        files.WriteAllBytes(UserIni, ini);
        var installation = new StarterNG.Tests.Fakes.TestInstallation(files);
        installation.Environment.With("HOME", "/home/kolejarz");
        installation.SettingsStore.Load();
        return installation;
    }

    private static bool Holds(byte[] file, byte[] part) => file.AsSpan().IndexOf(part) >= 0;

    [Theory]
    [InlineData("Obecný_monitor_PnP:1920,0")]
    [InlineData("Écran_à_Ábc:0,0")]
    [InlineData("Монитор_Рабочий:-1920,0")]
    [InlineData("显示器:0,0")]
    public void A_monitors_name_goes_into_the_file_as_the_utf8_the_simulator_compares(string id)
    {
        var cp1250 = StarterNG.Infrastructure.Adapters.LegacyText.CodePage1250;
        var installation = WithIni(cp1250.GetBytes("lang pl\n// zażółć\nfullscreen yes\n"));
        installation.SettingsStore.Settings.FullscreenMonitor = id;

        installation.SettingsStore.Save();

        byte[] saved = installation.Files.ReadAllBytes(UserIni);
        Assert.True(Holds(saved, System.Text.Encoding.UTF8.GetBytes("fullscreenmonitor " + id)));
        // The rest of the file stays in the simulator's code page.
        Assert.True(Holds(saved, cp1250.GetBytes("// zażółć")));
        Assert.Equal(id, WithIni(saved).SettingsStore.Settings.FullscreenMonitor);
    }

    [Fact]
    public void A_monitors_name_written_in_the_code_page_is_read_and_goes_as_utf8_on_a_save()
    {
        const string id = "Obecný_monitor_PnP:1920,0";
        var cp1250 = StarterNG.Infrastructure.Adapters.LegacyText.CodePage1250;
        var installation = WithIni(cp1250.GetBytes($"fullscreen yes\nfullscreenmonitor {id}\n"));
        Assert.Equal(id, installation.SettingsStore.Settings.FullscreenMonitor);

        installation.SettingsStore.Save();

        byte[] saved = installation.Files.ReadAllBytes(UserIni);
        Assert.True(Holds(saved, System.Text.Encoding.UTF8.GetBytes("fullscreenmonitor " + id)));
        Assert.False(Holds(saved, cp1250.GetBytes("fullscreenmonitor " + id)));
    }

    [Fact]
    public void An_ascii_name_of_a_monitor_stays_as_the_file_has_it()
    {
        var installation = WithIni(System.Text.Encoding.ASCII.GetBytes("fullscreenmonitor   HDMI-1:1920,0\n"));

        installation.SettingsStore.Save();

        Assert.True(Holds(installation.Files.ReadAllBytes(UserIni),
                          System.Text.Encoding.ASCII.GetBytes("fullscreenmonitor   HDMI-1:1920,0")));
    }
}
