using StarterNG.Domain.Settings;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class SettingReachTests
{
    private static SimulatorTarget Linux(string renderer = "full", bool server = false) =>
        new(WindowsBuild: false, renderer, server);

    [Theory]
    [InlineData("feedbackmode")]
    [InlineData("feedbackport")]
    [InlineData("showsystemconsole")]
    [InlineData("gfx.angleplatform")]
    public void Windows_only_keys_idle_on_a_linux_build(string key)
    {
        Assert.Equal(IdleReason.WindowsBuildOnly, SettingReach.Why(key, Linux()));
        Assert.Equal(IdleReason.None, SettingReach.Why(key, Linux() with { WindowsBuild = true }));
    }

    [Theory]
    [InlineData("full", IdleReason.LegacyRendererOnly)]
    [InlineData("experimental", IdleReason.LegacyRendererOnly)]
    [InlineData("legacy", IdleReason.None)]
    [InlineData("simpleshader", IdleReason.None)]
    [InlineData("simple", IdleReason.None)]
    public void Legacy_renderer_keys_follow_the_renderer(string renderer, IdleReason expected)
    {
        Assert.Equal(expected, SettingReach.Why("smoothtraction", Linux(renderer)));
        Assert.Equal(expected, SettingReach.Why("usevbo", Linux(renderer)));
        Assert.Equal(expected, SettingReach.Why("gfx.shadows.cab.range", Linux(renderer)));
    }

    [Fact]
    public void Pausing_in_the_background_needs_a_network_server()
    {
        Assert.Equal(IdleReason.NeedsNetworkServer, SettingReach.Why("inactivepause", Linux()));
        Assert.Equal(IdleReason.None, SettingReach.Why("inactivepause", Linux(server: true)));
    }

    [Fact]
    public void The_starters_own_keys_never_idle()
    {
        Assert.Equal(IdleReason.None, SettingReach.Why("starter.autoclose", Linux()));
    }

    [Theory]
    [InlineData("width, height")]
    [InlineData("fullscreen")]
    public void The_monitors_own_mode_overrides_size_and_full_screen(string key)
    {
        Assert.Equal(IdleReason.MonitorModeInstead,
                     SettingReach.Why(key, Linux() with { FullscreenWindowed = true }));
        Assert.Equal(IdleReason.None, SettingReach.Why(key, Linux()));
    }

    [Fact]
    public void An_ordinary_key_works_everywhere()
    {
        Assert.Equal(IdleReason.None, SettingReach.Why("fieldofview", Linux()));
    }

    [Theory]
    [InlineData("full", IdleReason.None)]
    [InlineData("legacy", IdleReason.ModernRendererOnly)]
    [InlineData("experimental", IdleReason.ModernRendererOnly)]
    public void Effects_of_the_full_renderer_follow_the_renderer(string renderer, IdleReason expected)
    {
        Assert.Equal(expected, SettingReach.Why("gfx.postfx.motionblur.enabled", Linux(renderer)));
        Assert.Equal(expected, SettingReach.Why("gfx.envmap.enabled", Linux(renderer)));
        Assert.Equal(expected, SettingReach.Why("sleeperdistance", Linux(renderer)));
    }

    [Theory]
    [InlineData("vulkan", IdleReason.None)]
    [InlineData("vulkan14", IdleReason.None)]
    [InlineData("full", IdleReason.VulkanOnly)]
    [InlineData("gl46", IdleReason.VulkanOnly)]
    [InlineData("legacy", IdleReason.VulkanOnly)]
    public void What_only_vulkan_reads_follows_the_renderer(string renderer, IdleReason expected)
    {
        Assert.Equal(expected, SettingReach.Why("gfx.dlss.preset", Linux(renderer)));
        Assert.Equal(expected, SettingReach.Why("gfx.framegeneration", Linux(renderer)));
        Assert.Equal(expected, SettingReach.Why("gfx.raytracing", Linux(renderer)));
        // Both renderers of the new simulator read this one.
        Assert.Equal(IdleReason.None, SettingReach.Why("gfx.postfx.ao", Linux(renderer)));
    }

    [Theory]
    [InlineData("vulkan")]
    [InlineData("vulkan14")]
    [InlineData("opengl")]
    [InlineData("gl46")]
    public void The_new_simulators_renderers_read_what_the_full_one_does(string renderer)
    {
        Assert.Equal(IdleReason.None, SettingReach.Why("gfx.envmap.enabled", Linux(renderer)));
        Assert.Equal(IdleReason.None, SettingReach.Why("multisampling", Linux(renderer)));
    }

    [Theory]
    [InlineData("full", IdleReason.None)]
    [InlineData("legacy", IdleReason.None)]
    [InlineData("experimental", IdleReason.OpenGlRenderersOnly)]
    public void The_experimental_renderer_passes_over_what_only_opengl_reads(string renderer, IdleReason expected)
    {
        Assert.Equal(expected, SettingReach.Why("multisampling", Linux(renderer)));
        Assert.Equal(expected, SettingReach.Why("anisotropicfiltering", Linux(renderer)));
        Assert.Equal(expected, SettingReach.Why("gfx.smoke", Linux(renderer)));
        // ...but reads the rest of the reflections and shadows.
        Assert.Equal(IdleReason.None, SettingReach.Why("gfx.reflections.fidelity", Linux(renderer)));
        Assert.Equal(IdleReason.None, SettingReach.Why("shadowtune", Linux(renderer)));
    }

    [Theory]
    [InlineData(null, 0, false)]
    [InlineData(0.0, 0, false)]
    [InlineData(60.0, 0, true)]
    [InlineData(null, 50, true)]   // fpsaverage, the older regulator
    public void The_draw_range_factor_needs_a_regulator(double? targetFps, double fpsAverage, bool regulated)
    {
        var target = Linux() with { TargetFps = targetFps, FpsAverage = fpsAverage };

        Assert.Equal(regulated ? IdleReason.None : IdleReason.NeedsFrameRateTarget,
                     SettingReach.Why("gfx.drawrange.factor.max", target));
    }

    // ── The rest of the target ───────────────────────────────────────────────

    [Fact]
    public void A_windows_build_is_told_from_its_header()
    {
        var files = new InMemoryFileSystem()
            .WithExecutable(TestInstallation.At("eu07"), "\u007fELF binary")
            .WithFile(TestInstallation.At("eu07.exe"), "MZ binary");
        var installation = new TestInstallation(files);

        Assert.False(installation.Executables.IsWindowsBuild("eu07"));
        Assert.True(installation.Executables.IsWindowsBuild("eu07.exe"));
    }

    [Fact]
    public void A_network_server_in_the_ini_is_noticed()
    {
        string ini = Path.Combine("/home/kolejarz", ".config", "MaSzyna", "eu07.ini");
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(ini, "network.server tcp 0.0.0.0:7424\n"));
        installation.Environment.With("HOME", "/home/kolejarz");

        installation.SettingsStore.Load();

        Assert.True(installation.SettingsStore.Settings.NetworkServer);
    }
}
