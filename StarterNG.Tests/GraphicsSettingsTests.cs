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

    [Fact]
    public void The_ao_lighting_and_cab_probe_keys_survive_a_round_trip_in_the_simulators_form()
    {
        var s = Read("gfx.postfx.ao.lighting no\ngfx.cabprobe high\ngfx.cabprobe.interval 12\ngfx.cabprobe.bounce 2.5\ngfx.cabprobe.reflections no\n");

        Assert.False(s.AoLighting);
        Assert.Equal(3, s.CabProbe);
        Assert.Equal(12, s.CabProbeInterval);
        Assert.Equal(2.5, s.CabProbeBounce);
        Assert.False(s.CabProbeReflections);

        string text = Write(s);
        Assert.Equal("gfx.postfx.ao.lighting no", Line(text, "gfx.postfx.ao.lighting"));
        Assert.Equal("gfx.cabprobe high", Line(text, "gfx.cabprobe"));
        Assert.Equal("gfx.cabprobe.interval 12", Line(text, "gfx.cabprobe.interval"));
        Assert.Equal("gfx.cabprobe.bounce 2.5", Line(text, "gfx.cabprobe.bounce"));
        Assert.Equal("gfx.cabprobe.reflections no", Line(text, "gfx.cabprobe.reflections"));

        var again = Read(text);
        Assert.Equal((s.AoLighting, s.CabProbe, s.CabProbeInterval, s.CabProbeBounce, s.CabProbeReflections),
                     (again.AoLighting, again.CabProbe, again.CabProbeInterval, again.CabProbeBounce, again.CabProbeReflections));
    }

    [Fact]
    public void Missing_ao_lighting_and_cab_probe_keys_take_the_simulators_defaults_and_are_written_like_its_export()
    {
        var s = Read("");

        Assert.Equal((true, 2, 4, 1.0, true), (s.AoLighting, s.CabProbe, s.CabProbeInterval, s.CabProbeBounce, s.CabProbeReflections));

        string text = Write(s);
        Assert.Equal("gfx.postfx.ao.lighting yes", Line(text, "gfx.postfx.ao.lighting"));
        Assert.Equal("gfx.cabprobe medium", Line(text, "gfx.cabprobe"));
        Assert.Equal("gfx.cabprobe.interval 4", Line(text, "gfx.cabprobe.interval"));
        Assert.Equal("gfx.cabprobe.bounce 1", Line(text, "gfx.cabprobe.bounce"));
        Assert.Equal("gfx.cabprobe.reflections yes", Line(text, "gfx.cabprobe.reflections"));
    }

    [Fact]
    public void Unknown_ao_lighting_and_cab_probe_values_fall_back_to_the_simulators_defaults()
    {
        var s = Read("gfx.postfx.ao.lighting maybe\ngfx.cabprobe bogus\ngfx.cabprobe.interval abc\ngfx.cabprobe.bounce x\ngfx.cabprobe.reflections ?\n");

        Assert.Equal((true, 2, 4, 1.0, true), (s.AoLighting, s.CabProbe, s.CabProbeInterval, s.CabProbeBounce, s.CabProbeReflections));
    }

    [Theory]
    [InlineData("no", 0)]
    [InlineData("0", 0)]
    [InlineData("low", 1)]
    [InlineData("1", 1)]
    [InlineData("medium", 2)]
    [InlineData("yes", 2)]
    [InlineData("2", 2)]
    [InlineData("high", 3)]
    [InlineData("3", 3)]
    public void The_cab_probe_level_is_read_with_the_aliases_the_simulator_takes(string value, int expected)
    {
        Assert.Equal(expected, Read($"gfx.cabprobe {value}\n").CabProbe);
    }

    [Fact]
    public void The_cab_probe_numbers_are_kept_within_the_simulators_ranges()
    {
        var s = Read("gfx.cabprobe.interval 500\ngfx.cabprobe.bounce 9\n");
        Assert.Equal((120, 4.0), (s.CabProbeInterval, s.CabProbeBounce));

        s = Read("gfx.cabprobe.interval 0\ngfx.cabprobe.bounce -1\n");
        Assert.Equal((1, 0.0), (s.CabProbeInterval, s.CabProbeBounce));
    }

    [Fact]
    public void The_ao_lighting_counts_with_gtao_only()
    {
        Assert.True(GraphicsRules.AoLightingUsed(Read("gfx.postfx.ao gtao\n")));
        Assert.False(GraphicsRules.AoLightingUsed(Read("gfx.postfx.ao ssao\n")));
        Assert.False(GraphicsRules.AoLightingUsed(Read("gfx.postfx.ao no\n")));
    }

    [Theory]
    [InlineData("gfx.ssr no\ngfx.contactshadows no\n", true)]
    [InlineData("gfx.ssr low\ngfx.contactshadows no\n", false)]
    [InlineData("gfx.ssr no\ngfx.contactshadows low\n", false)]
    [InlineData("gfx.ssr no\ngfx.contactshadows no\ngfx.depthprepass.scene yes\n", false)]
    [InlineData("gfx.ssr no\ngfx.contactshadows no\ngfx.postfx.ao.lighting no\n", false)]
    [InlineData("gfx.ssr no\ngfx.contactshadows no\ngfx.postfx.ao ssao\n", false)]
    public void The_ao_lighting_adds_the_depth_pass_only_when_nothing_else_draws_it(string text, bool expected)
    {
        Assert.Equal(expected, GraphicsRules.AoLightingAddsDepthPrepass(Read(text)));
    }

    [Theory]
    [InlineData("gfx.cabprobe medium\n", true, true)]
    [InlineData("gfx.cabprobe no\n", false, false)]
    [InlineData("gfx.cabprobe low\ngfx.lights.clustered no\n", true, false)]
    [InlineData("gfx.cabprobe high\ngfx.lights.cab no\n", true, false)]
    public void The_cab_probe_lights_the_cab_only_with_its_lamp_as_a_light(string text, bool used, bool lights)
    {
        var s = Read(text);

        Assert.Equal(used, GraphicsRules.CabProbeUsed(s));
        Assert.Equal(lights, GraphicsRules.CabProbeLights(s));
    }

    [Theory]
    [InlineData(GraphicsPreset.Lowest, false, 0, 4, false)]
    [InlineData(GraphicsPreset.Low, false, 0, 4, false)]
    [InlineData(GraphicsPreset.Medium, false, 1, 8, true)]
    [InlineData(GraphicsPreset.High, true, 2, 4, true)]
    [InlineData(GraphicsPreset.Ultra, true, 3, 2, true)]
    public void A_preset_sets_the_ao_lighting_and_the_cab_probe(GraphicsPreset preset, bool aoLighting, int probe, int interval, bool reflections)
    {
        var s = new SimulatorSettings { CabProbeBounce = 2.5 };

        GraphicsPresets.Apply(s, preset, true);

        Assert.Equal((aoLighting, probe, interval, reflections), (s.AoLighting, s.CabProbe, s.CabProbeInterval, s.CabProbeReflections));
        Assert.Equal(2.5, s.CabProbeBounce);
        Assert.Equal(preset, GraphicsPresets.Detect(s, true));

        string text = Write(s);
        Assert.Equal(preset, GraphicsPresets.Detect(Read(text), true));
    }

    [Fact]
    public void An_import_keeps_the_lamp_switch_of_the_users_own_file()
    {
        var installation = WithIni("gfx.lights.cab no\n");
        installation.Files.WithFile("/backup/eu07.ini", "gfx.lights.cab yes\ngfx.cabprobe high\n");

        Assert.True(installation.SettingsStore.ImportFrom("/backup/eu07.ini"));

        Assert.False(installation.SettingsStore.Settings.CabLampLight);
        Assert.Equal(3, installation.SettingsStore.Settings.CabProbe);
    }
}
