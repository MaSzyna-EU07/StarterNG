using StarterNG.Classes;
using StarterNG.Domain.Settings;
using StarterNG.Infrastructure.Settings;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class GraphicsSettingsTests
{
    private static readonly string UserIni = Path.Combine("/home/kolejarz", ".config", "MaSzyna", "eu07.ini");

    private static TestInstallation WithIni(string text)
    {
        var installation = new TestInstallation(new InMemoryFileSystem().WithFile(UserIni, text));
        installation.Environment.With("HOME", "/home/kolejarz");
        installation.SettingsStore.Load();
        return installation;
    }

    private static SimulatorSettings Read(string text, bool dlssPlatform = true)
    {
        var s = new SimulatorSettings();
        new SettingsSerializer(dlssPlatform).Read(s, ConfigFile.Parse(text));
        return s;
    }

    private static string Write(SimulatorSettings s, string text = "", bool dlssPlatform = true)
    {
        var c = ConfigFile.Parse(text);
        new SettingsSerializer(dlssPlatform).Write(s, c);
        return c.ToText();
    }

    private static string[] Lines(string text) =>
        text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

    private static string? Line(string text, string key) =>
        Lines(text).FirstOrDefault(l => l == key || l.StartsWith(key + " ", StringComparison.Ordinal));

    [Theory]
    [InlineData("full", GraphicsBackend.OpenGL)]
    [InlineData("default", GraphicsBackend.OpenGL)]
    [InlineData("gl46", GraphicsBackend.OpenGL)]
    [InlineData("legacy", GraphicsBackend.OpenGL)]
    [InlineData("experimental", GraphicsBackend.OpenGL)]
    [InlineData("vulkan", GraphicsBackend.Vulkan)]
    [InlineData("vulkan14", GraphicsBackend.Vulkan)]
    public void The_renderer_is_read_with_the_aliases_the_simulator_takes(string value, GraphicsBackend expected)
    {
        Assert.Equal(expected, Read($"gfxrenderer {value}\n").Backend);
    }

    [Fact]
    public void The_renderer_is_written_as_a_name_every_simulator_reads()
    {
        Assert.Equal("gfxrenderer full", Line(Write(Read("gfxrenderer legacy\n")), "gfxrenderer"));
        Assert.Equal("gfxrenderer vulkan", Line(Write(Read("gfxrenderer vulkan14\n")), "gfxrenderer"));
    }

    [Fact]
    public void The_new_graphics_keys_are_read()
    {
        var s = Read("gfxrenderer vulkan\n" +
                     "gfx.antialiasing dlss\ngfx.dlss.preset K\ngfx.framegeneration x3\ngfx.reflex boost\n" +
                     "gfx.raytracing yes\ngfx.raytracing.range 800\ngfx.ssr high\ngfx.postfx.ao ssao\ngfx.postfx.ao.quality 4 6\n" +
                     "gfx.lights.shadows low\ngfx.contactshadows 3\ngfx.fog analytic\ngfx.fog.quality ultra\n" +
                     "gfx.clouds ultra\ngfx.clouds.shadows no\ngfx.grass no\ngfx.grass.distance 120\ngfx.grass.density 1.5\n" +
                     "gfx.buffer.percent 0.67\ngfx.vulkan.device RTX\n");

        Assert.Equal(GraphicsBackend.Vulkan, s.Backend);
        Assert.Equal(AntiAliasingMode.Dlss, s.AntiAliasing);
        Assert.Equal("k", s.DlssPreset);
        Assert.Equal(3, s.FrameGeneration);
        Assert.Equal(2, s.Reflex);
        Assert.True(s.RayTracing);
        Assert.Equal(800, s.RayTracingRange);
        Assert.Equal(3, s.Ssr);
        Assert.Equal(1, s.AmbientOcclusion);
        Assert.Equal((4, 6), (s.AoSlices, s.AoSteps));
        Assert.Equal(1, s.LightShadows);
        Assert.Equal(3, s.ContactShadows);
        Assert.False(s.VolumetricFog);
        Assert.Equal(4, s.FogQuality);
        Assert.Equal(4, s.Clouds);
        Assert.False(s.CloudShadows);
        Assert.False(s.Grass);
        Assert.Equal(120, s.GrassDistance);
        Assert.Equal(1.5, s.GrassDensity);
        Assert.Equal(67, s.BufferScalePercent);
        Assert.Equal("RTX", s.VulkanDevice);
    }

    [Fact]
    public void Missing_keys_take_the_simulators_defaults()
    {
        var s = Read("");

        Assert.Equal(GraphicsBackend.OpenGL, s.Backend);
        Assert.Equal(AntiAliasingMode.Msaa4, s.AntiAliasing);
        Assert.Equal(2, s.Ssr);
        Assert.Equal(2, s.AmbientOcclusion);
        Assert.Equal(2, s.LightShadows);
        Assert.Equal(0, s.ContactShadows);
        Assert.True(s.VolumetricFog);
        Assert.Equal(2, s.FogQuality);
        Assert.Equal(2, s.Clouds);
        Assert.False(s.RayTracing);
        Assert.Equal(1, s.FrameGeneration);
    }

    [Fact]
    public void The_tokens_written_are_the_ones_the_simulator_parses()
    {
        var s = Read("gfxrenderer vulkan\n");
        s.AntiAliasing = AntiAliasingMode.Dlaa;
        s.FrameGeneration = 4;
        s.Reflex = 1;
        s.LightShadows = 3;
        s.ContactShadows = 0;
        s.Clouds = 4;
        s.VolumetricFog = false;
        s.FogQuality = 1;
        s.AmbientOcclusion = 1;
        s.Ssr = 2;
        s.RayTracing = true;

        string text = Write(s);

        Assert.Equal("gfx.antialiasing dlaa", Line(text, "gfx.antialiasing"));
        Assert.Equal("multisampling 0", Line(text, "multisampling"));
        Assert.Equal("gfx.framegeneration x4", Line(text, "gfx.framegeneration"));
        Assert.Equal("gfx.reflex on", Line(text, "gfx.reflex"));
        Assert.Equal("gfx.lights.shadows high", Line(text, "gfx.lights.shadows"));
        Assert.Equal("gfx.contactshadows no", Line(text, "gfx.contactshadows"));
        Assert.Equal("gfx.clouds ultra", Line(text, "gfx.clouds"));
        Assert.Equal("gfx.fog analytic", Line(text, "gfx.fog"));
        Assert.Equal("gfx.fog.quality low", Line(text, "gfx.fog.quality"));
        Assert.Equal("gfx.postfx.ao ssao", Line(text, "gfx.postfx.ao"));
        Assert.Equal("gfx.ssr medium", Line(text, "gfx.ssr"));
        Assert.Equal("gfx.raytracing yes", Line(text, "gfx.raytracing"));
        Assert.Equal("gfx.buffer.percent 100", Line(text, "gfx.buffer.percent"));
    }

    [Fact]
    public void Msaa_is_written_to_both_keys_for_older_simulators()
    {
        var s = Read("multisampling 1\n");
        Assert.Equal(AntiAliasingMode.Msaa2, s.AntiAliasing);

        string text = Write(s);

        Assert.Equal("multisampling 1", Line(text, "multisampling"));
        Assert.Equal("gfx.antialiasing msaax2", Line(text, "gfx.antialiasing"));
    }

    [Fact]
    public void Of_the_two_anti_aliasing_keys_the_later_one_wins_as_in_the_simulator()
    {
        Assert.Equal(AntiAliasingMode.Taa, Read("multisampling 2\ngfx.antialiasing taa\n").AntiAliasing);
        Assert.Equal(AntiAliasingMode.Msaa4, Read("gfx.antialiasing taa\nmultisampling 2\n").AntiAliasing);
    }

    [Fact]
    public void Gfx_antialiasing_is_moved_below_multisampling_so_it_takes_effect()
    {
        var s = Read("gfx.antialiasing msaax4\nmultisampling 2\n");
        s.AntiAliasing = AntiAliasingMode.Taa;

        string text = Write(s, "gfx.antialiasing msaax4\nmultisampling 2\n");
        var lines = Lines(text).ToList();

        Assert.True(lines.IndexOf("gfx.antialiasing taa") > lines.IndexOf("multisampling 0"));
        Assert.Equal(AntiAliasingMode.Taa, Read(text).AntiAliasing);
    }

    [Fact]
    public void On_opengl_the_vulkan_only_features_are_written_off()
    {
        var s = Read("gfxrenderer full\ngfx.antialiasing dlss\ngfx.framegeneration x2\ngfx.reflex on\ngfx.raytracing yes\n");

        Assert.Equal(AntiAliasingMode.Taa, s.AntiAliasing);
        Assert.Equal(1, s.FrameGeneration);
        Assert.Equal(0, s.Reflex);
        Assert.False(s.RayTracing);

        string text = Write(s);
        Assert.Equal("gfx.antialiasing taa", Line(text, "gfx.antialiasing"));
        Assert.Equal("gfx.framegeneration off", Line(text, "gfx.framegeneration"));
        Assert.Equal("gfx.reflex off", Line(text, "gfx.reflex"));
        Assert.Equal("gfx.raytracing no", Line(text, "gfx.raytracing"));
    }

    [Fact]
    public void Frame_generation_needs_dlss_even_on_vulkan()
    {
        var s = Read("gfxrenderer vulkan\ngfx.antialiasing taa\ngfx.framegeneration x2\ngfx.reflex on\n");

        Assert.Equal(1, s.FrameGeneration);
        Assert.Equal(1, s.Reflex);
        Assert.False(GraphicsRules.ReflexForced(s, true));
    }

    [Fact]
    public void Frame_generation_turns_reflex_on()
    {
        var s = Read("gfxrenderer vulkan\ngfx.antialiasing dlss\ngfx.framegeneration x2\n");

        Assert.True(GraphicsRules.ReflexForced(s, true));
    }

    [Fact]
    public void Without_a_dlss_platform_dlss_falls_back_to_taa()
    {
        var s = Read("gfxrenderer vulkan\ngfx.antialiasing dlss\n", dlssPlatform: false);

        Assert.Equal(AntiAliasingMode.Taa, s.AntiAliasing);
    }

    [Fact]
    public void Dlaa_ignores_the_render_scale()
    {
        var s = Read("gfxrenderer vulkan\ngfx.antialiasing dlaa\n");
        Assert.False(GraphicsRules.BufferScaleUsed(s, true));

        s.AntiAliasing = AntiAliasingMode.Dlss;
        Assert.True(GraphicsRules.BufferScaleUsed(s, true));
    }

    [Fact]
    public void Sizes_are_rounded_down_to_what_the_simulator_uses()
    {
        var s = Read("maxtexturesize 16384\nmaxcabtexturesize 3000\nshadowtune 3000 0 250 0\n");

        Assert.Equal(8192, s.MaxTextureSize);
        Assert.Equal(2048, s.MaxCabTextureSize);
        Assert.Equal(2048, s.ShadowMapResolution);
    }

    [Fact]
    public void Keys_the_simulator_dropped_are_commented_out()
    {
        const string old = "usevbo yes\ngfx.angleplatform vulkan\ngfx.usegles no\ngfx.framebuffer.width 1920\ngfx.framebuffer.height 1080\n";

        string text = Write(Read(old), old);

        foreach (string key in new[] { "usevbo", "gfx.angleplatform", "gfx.usegles", "gfx.framebuffer.width", "gfx.framebuffer.height" })
        {
            Assert.Null(Line(text, key));
            Assert.Contains(Lines(text), l => l.StartsWith("// " + key, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void The_render_scale_reads_the_old_starter_key_when_the_new_one_is_missing()
    {
        Assert.Equal(75, Read("starter.bufferscale 75\n").BufferScalePercent);
        Assert.Equal(50, Read("starter.bufferscale 75\ngfx.buffer.percent 50\n").BufferScalePercent);
    }

    [Theory]
    [InlineData(GraphicsPreset.Lowest)]
    [InlineData(GraphicsPreset.Low)]
    [InlineData(GraphicsPreset.Medium)]
    [InlineData(GraphicsPreset.High)]
    [InlineData(GraphicsPreset.Ultra)]
    public void A_preset_is_recognised_after_it_is_applied_and_survives_a_round_trip(GraphicsPreset preset)
    {
        foreach (var backend in new[] { GraphicsBackend.OpenGL, GraphicsBackend.Vulkan })
        {
            var s = new SimulatorSettings { Backend = backend };
            GraphicsPresets.Apply(s, preset, true);

            Assert.Equal(preset, GraphicsPresets.Detect(s, true));
            Assert.Equal(preset, GraphicsPresets.Detect(Read(Write(s)), true));
        }
    }

    [Fact]
    public void A_change_after_a_preset_makes_the_settings_custom()
    {
        var s = new SimulatorSettings();
        GraphicsPresets.Apply(s, GraphicsPreset.High, true);
        s.Clouds = 4;

        Assert.Equal(GraphicsPreset.Custom, GraphicsPresets.Detect(s, true));
    }

    [Fact]
    public void Presets_leave_alone_what_is_not_a_matter_of_quality()
    {
        var s = new SimulatorSettings { Width = 2560, Height = 1440, BufferScalePercent = 67, VSync = true, FieldOfView = 60, MotionBlur = false };

        GraphicsPresets.Apply(s, GraphicsPreset.Lowest, true);

        Assert.Equal((2560, 1440, 67, true, 60, false), (s.Width, s.Height, s.BufferScalePercent, s.VSync, s.FieldOfView, s.MotionBlur));
    }

    [Fact]
    public void Ray_tracing_comes_with_ultra_on_vulkan_only()
    {
        var vulkan = new SimulatorSettings { Backend = GraphicsBackend.Vulkan };
        var opengl = new SimulatorSettings { Backend = GraphicsBackend.OpenGL };

        GraphicsPresets.Apply(vulkan, GraphicsPreset.Ultra, true);
        GraphicsPresets.Apply(opengl, GraphicsPreset.Ultra, true);

        Assert.True(vulkan.RayTracing);
        Assert.False(opengl.RayTracing);
    }

    [Fact]
    public void A_preset_keeps_dlss_on_vulkan()
    {
        var s = new SimulatorSettings { Backend = GraphicsBackend.Vulkan, AntiAliasing = AntiAliasingMode.Dlss };

        GraphicsPresets.Apply(s, GraphicsPreset.Ultra, true);

        Assert.Equal(AntiAliasingMode.Dlss, s.AntiAliasing);
        Assert.Equal(GraphicsPreset.Ultra, GraphicsPresets.Detect(s, true));
    }

    [Fact]
    public void An_import_takes_the_settings_but_keeps_this_installations_simulator_and_the_unmanaged_lines()
    {
        var installation = WithIni("starter.exe.auto no\nstarter.exe.path eu07_local.exe\ncalibratein 0 0 0 0\ngfx.clouds low\n");
        installation.Files.WithFile("/backup/eu07.ini", "starter.exe.auto no\nstarter.exe.path eu07_other.exe\ngfx.clouds ultra\ngfxrenderer vulkan\n");

        Assert.True(installation.SettingsStore.ImportFrom("/backup/eu07.ini"));
        var s = installation.SettingsStore.Settings;
        Assert.Equal(4, s.Clouds);
        Assert.Equal(GraphicsBackend.Vulkan, s.Backend);
        Assert.Equal("eu07_local.exe", s.ExecutablePath);

        // Nothing is written until saved, and the save keeps the user's own lines.
        Assert.Contains("gfx.clouds low", installation.Files.ReadAllText(UserIni));
        installation.SettingsStore.Save();
        string saved = installation.Files.ReadAllText(UserIni);
        Assert.Contains("calibratein 0 0 0 0", saved);
        Assert.Contains("gfx.clouds ultra", saved);
        Assert.Contains("starter.exe.path eu07_local.exe", saved);
    }

    [Fact]
    public void An_import_of_a_missing_file_changes_nothing()
    {
        var installation = WithIni("gfx.clouds low\n");

        Assert.False(installation.SettingsStore.ImportFrom("/nowhere/eu07.ini"));
        Assert.Equal(1, installation.SettingsStore.Settings.Clouds);
    }

    [Fact]
    public void An_export_writes_a_complete_file_elsewhere_and_leaves_the_users_one_alone()
    {
        var installation = WithIni("calibratein 0 0 0 0\ngfx.clouds low\n");
        installation.SettingsStore.Settings.Clouds = 3;

        Assert.True(installation.SettingsStore.ExportTo("/export/eu07.ini"));

        string exported = installation.Files.ReadAllText("/export/eu07.ini");
        Assert.Contains("calibratein 0 0 0 0", exported);
        Assert.Contains("gfx.clouds high", exported);
        Assert.Contains("gfx.clouds low", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void The_dlss_libraries_are_looked_for_next_to_a_windows_build()
    {
        var files = new InMemoryFileSystem()
            .WithDirectory(TestInstallation.Root)
            .WithExecutable(TestInstallation.At("eu07.exe"), "MZ binary")
            .WithFile(TestInstallation.At("nvngx_dlss.dll"), "dll");
        var installation = new TestInstallation(files);

        Assert.Equal((true, false), installation.Executables.DlssLibraries(TestInstallation.At("eu07.exe")));

        files.WithFile(TestInstallation.At("nvngx_dlssg.dll"), "dll");
        Assert.Equal((true, true), installation.Executables.DlssLibraries(TestInstallation.At("eu07.exe")));
    }

    [Fact]
    public void The_dlss_libraries_of_a_linux_build_are_the_shared_objects()
    {
        var files = new InMemoryFileSystem()
            .WithDirectory(TestInstallation.Root)
            .WithExecutable(TestInstallation.At("eu07"), "\u007fELF binary")
            .WithFile(TestInstallation.At("libnvidia-ngx-dlssg.so.310.4.0"), "so");
        var installation = new TestInstallation(files);

        Assert.Equal((false, true), installation.Executables.DlssLibraries(TestInstallation.At("eu07")));
    }

    [Fact]
    public void A_later_line_moved_up_keeps_the_index_of_every_key()
    {
        var c = ConfigFile.Parse("a 1\nb 2\nc 3\n");

        c.MoveAfter("a", "c");

        Assert.Equal("b 2\nc 3\na 1\n", c.ToText().Replace("\r\n", "\n"));
        Assert.True(c.LineOf("a") > c.LineOf("c"));
        c.Set("b", "9");
        Assert.Equal("b 9\nc 3\na 1\n", c.ToText().Replace("\r\n", "\n"));
    }
}
