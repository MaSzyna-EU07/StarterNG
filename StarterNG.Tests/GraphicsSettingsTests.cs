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

    private static SimulatorSettings Read(string text)
    {
        var s = new SimulatorSettings();
        new SettingsSerializer().Read(s, ConfigFile.Parse(text));
        return s;
    }

    private static string Write(SimulatorSettings s, string text = "")
    {
        var c = ConfigFile.Parse(text);
        new SettingsSerializer().Write(s, c);
        return c.ToText();
    }

    private static string[] Lines(string text) =>
        text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

    private static string? Line(string text, string key) =>
        Lines(text).FirstOrDefault(l => l == key || l.StartsWith(key + " ", StringComparison.Ordinal));

    // As application.cpp on master has them; a word the starter does not know is the file's own, written back as it is.
    [Theory]
    [InlineData("default", "default")]
    [InlineData("full", "full")]
    [InlineData("gl46", "gl46")]
    [InlineData("vulkan", "vulkan")]
    [InlineData("vulkan14", "vulkan14")]
    [InlineData("legacy", "legacy")]
    [InlineData("simpleshader", "legacy")]
    [InlineData("off", "legacy")]
    [InlineData("simple", "simple")]
    [InlineData("experimental", "experimental")]
    public void A_renderer_is_written_by_the_name_master_knows(string read, string written)
    {
        Assert.Equal($"gfxrenderer {written}", Line(Write(Read($"gfxrenderer {read}\n")), "gfxrenderer"));
    }

    [Fact]
    public void Without_a_renderer_in_the_file_the_full_one_is_chosen()
    {
        Assert.Equal(0, Read("").RenderEngine);
        Assert.Equal("gfxrenderer default", Line(Write(Read("")), "gfxrenderer"));
    }

    [Fact]
    public void An_older_renderer_stays_chosen_and_can_be_chosen_again()
    {
        var installation = WithIni("gfxrenderer legacy\n");
        var store = installation.SettingsStore;

        store.Settings.FieldOfView = 50;
        store.Save();
        Assert.Contains("gfxrenderer legacy", installation.Files.ReadAllText(UserIni));

        store.Settings.RenderEngine = SimulatorSettings.RenderEngineOf("simple");
        store.Save();
        Assert.Contains("gfxrenderer simple", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void Keys_only_the_legacy_renderer_or_windows_read_stay_in_the_file()
    {
        string[] old =
        {
            "usevbo no", "gfx.angleplatform vulkan", "gfx.usegles yes", "gfx.shadows.cab.range 20"
        };
        var installation = WithIni(string.Join("\n", old) + "\n");
        installation.SettingsStore.Settings.FieldOfView = 50;

        installation.SettingsStore.Save();

        string saved = installation.Files.ReadAllText(UserIni);
        Assert.All(old, line => Assert.Contains(line, saved));
    }

    [Fact]
    public void Keys_of_the_vulkan_renderer_master_does_not_have_stay_on_a_save()
    {
        string[] vulkan =
        {
            "gfx.antialiasing dlss", "gfx.dlss.preset k", "gfx.framegeneration x2", "gfx.reflex on",
            "gfx.raytracing yes", "gfx.ssr high", "gfx.postfx.ao gtao", "gfx.clouds ultra", "gfx.grass yes",
            "gfx.buffer.percent 0.67", "gfx.vulkan.device RTX", "gfx.fog yes", "gfx.cabprobe medium"
        };
        var installation = WithIni("calibratein 0 0 0 0\n" + string.Join("\n", vulkan) + "\n");

        installation.SettingsStore.Save();

        string saved = installation.Files.ReadAllText(UserIni);
        Assert.Contains("calibratein 0 0 0 0", saved);
        // Written in by hand for a build that has that renderer: the starter does not know them, and leaves them.
        Assert.All(vulkan, line => Assert.Equal(line, Line(saved, line.Split(' ')[0])));
    }

    [Fact]
    public void Levels_leave_the_switches_only_an_older_simulator_reads()
    {
        var s = new SimulatorSettings { UseVbo = false, UseGLES = true, RenderAngleVulkan = true };

        GraphicsPreset.Ultra.ApplyTo(s);

        Assert.Equal((false, true, true), (s.UseVbo, s.UseGLES, s.RenderAngleVulkan));
    }

    [Theory]
    [InlineData("multisampling 0\n", AntiAliasingMode.Off)]
    [InlineData("multisampling 1\n", AntiAliasingMode.Msaa2)]
    [InlineData("multisampling 2\n", AntiAliasingMode.Msaa4)]
    [InlineData("multisampling 3\n", AntiAliasingMode.Msaa8)]
    [InlineData("", AntiAliasingMode.Msaa4)]
    public void Multisampling_is_read_as_master_reads_it(string text, AntiAliasingMode expected)
    {
        Assert.Equal(expected, Read(text).AntiAliasing);
    }

    [Fact]
    public void Multisampling_is_written_to_its_own_key_only()
    {
        var s = Read("multisampling 1\n");

        string text = Write(s);

        Assert.Equal("multisampling 1", Line(text, "multisampling"));
        Assert.Null(Line(text, "gfx.antialiasing"));
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
    public void The_render_scale_is_the_starters_own_key_or_the_framebuffer_against_the_window()
    {
        Assert.Equal(75, Read("starter.bufferscale 75\n").BufferScalePercent);
        Assert.Equal(50, Read("width 1920\ngfx.framebuffer.width 960\n").BufferScalePercent);
        Assert.Equal(100, Read("").BufferScalePercent);
    }

    [Fact]
    public void The_render_scale_is_written_as_the_framebuffer_master_reads()
    {
        var s = Read("width 1920\nheight 1080\nstarter.bufferscale 50\n");

        string text = Write(s);

        Assert.Equal("gfx.framebuffer.width 960", Line(text, "gfx.framebuffer.width"));
        Assert.Equal("gfx.framebuffer.height 540", Line(text, "gfx.framebuffer.height"));
        Assert.Null(Line(text, "gfx.buffer.percent"));
    }

    // Borderless, the simulator takes the window's size from the monitor (application.cpp) and
    // leaves width and height unread; -1 is its own word for a buffer of the window's size
    // (opengl33renderer.cpp).
    [Fact]
    public void A_borderless_screen_draws_into_a_buffer_of_the_windows_own_size()
    {
        var s = Read("fullscreenwindowed yes\nwidth 1280\nheight 720\n");

        string text = Write(s);

        Assert.Equal("gfx.framebuffer.width -1", Line(text, "gfx.framebuffer.width"));
        Assert.Equal("gfx.framebuffer.height -1", Line(text, "gfx.framebuffer.height"));
        Assert.Equal("width 1280", Line(text, "width"));
        Assert.Equal(100, Read(text).BufferScalePercent);
    }

    [Fact]
    public void A_borderless_screen_with_a_smaller_buffer_takes_its_part_of_the_monitor()
    {
        var s = Read("fullscreenwindowed yes\nfullscreenmonitor HDMI-1:1920,0\nwidth 1280\nheight 720\nstarter.bufferscale 50\n");
        var asked = new List<string>();
        var serializer = new SettingsSerializer(id =>
        {
            asked.Add(id);
            return (2560, 1440);
        });
        var c = new ConfigFile();

        serializer.Write(s, c);

        Assert.Equal("1280", c.GetRaw("gfx.framebuffer.width"));
        Assert.Equal("720", c.GetRaw("gfx.framebuffer.height"));
        Assert.Equal("50", c.GetRaw("starter.bufferscale"));
        Assert.Equal(new[] { "HDMI-1:1920,0" }, asked.Distinct());
        var read = new SimulatorSettings();
        serializer.Read(read, c);
        Assert.Equal(50, read.BufferScalePercent);
    }

    [Fact]
    public void A_borderless_screen_on_a_monitor_of_unknown_size_keeps_the_windows_own_buffer()
    {
        var s = Read("fullscreenwindowed yes\nwidth 1280\nheight 720\nstarter.bufferscale 50\n");
        var c = new ConfigFile();

        new SettingsSerializer(_ => null).Write(s, c);

        Assert.Equal("-1", c.GetRaw("gfx.framebuffer.width"));
        Assert.Equal("-1", c.GetRaw("gfx.framebuffer.height"));
    }

    [Fact]
    public void A_buffer_of_the_window_left_in_a_borderless_file_goes_on_a_save()
    {
        // As this starter wrote it before: the size of the resolution row, which borderless does not use.
        var installation = WithIni("fullscreenwindowed yes\nwidth 1280\nheight 720\nstarter.bufferscale 100\n" +
                                   "gfx.framebuffer.width 1280\ngfx.framebuffer.height 720\n");

        installation.SettingsStore.Save();

        string saved = installation.Files.ReadAllText(UserIni);
        Assert.Equal("gfx.framebuffer.width -1", Line(saved, "gfx.framebuffer.width"));
        Assert.Equal("gfx.framebuffer.height -1", Line(saved, "gfx.framebuffer.height"));
    }

    [Fact]
    public void A_buffer_the_file_has_for_a_window_or_a_full_screen_stays_until_changed()
    {
        var installation = WithIni("fullscreenwindowed no\nwidth 1280\nheight 720\nstarter.bufferscale 100\n" +
                                   "gfx.framebuffer.width 1280\ngfx.framebuffer.height 720\n");

        installation.SettingsStore.Save();

        string saved = installation.Files.ReadAllText(UserIni);
        Assert.Equal("gfx.framebuffer.width 1280", Line(saved, "gfx.framebuffer.width"));
        Assert.Equal("gfx.framebuffer.height 720", Line(saved, "gfx.framebuffer.height"));
    }

    [Fact]
    public void A_level_is_recognised_after_it_is_applied_and_survives_a_save()
    {
        for (int engine = 0; engine < SimulatorSettings.RenderEngines.Length; engine++)
        {
            foreach (var level in GraphicsPreset.All)
            {
                var installation = WithIni("");
                var store = installation.SettingsStore;
                store.Settings.RenderEngine = engine;
                level.ApplyTo(store.Settings);
                Assert.Same(level, GraphicsPreset.Matching(store.Settings));

                store.Save();

                var again = new TestInstallation(installation.Files);
                again.Environment.With("HOME", "/home/kolejarz");
                again.SettingsStore.Load();
                Assert.Same(level, GraphicsPreset.Matching(again.SettingsStore.Settings));
            }
        }
    }

    [Fact]
    public void A_change_after_a_level_leaves_the_level()
    {
        var s = new SimulatorSettings();
        GraphicsPreset.High.ApplyTo(s);
        s.DynamicLights = 2;

        Assert.Null(GraphicsPreset.Matching(s));
    }

    [Fact]
    public void Levels_leave_alone_what_is_not_a_matter_of_quality()
    {
        var s = new SimulatorSettings { Width = 2560, Height = 1440, VSync = true, FieldOfView = 60, MotionBlur = false };

        GraphicsPreset.Low.ApplyTo(s);

        Assert.Equal((2560, 1440, true, 60, false), (s.Width, s.Height, s.VSync, s.FieldOfView, s.MotionBlur));
    }

    [Fact]
    public void No_level_leaves_decorative_consists_out()
    {
        foreach (var level in GraphicsPreset.All)
        {
            var s = new SimulatorSettings { IgnoreIrrelevantTrains = false };
            level.ApplyTo(s);
            Assert.False(s.IgnoreIrrelevantTrains, level.NameKey);

            s.IgnoreIrrelevantTrains = true;
            level.ApplyTo(s);
            Assert.True(s.IgnoreIrrelevantTrains, level.NameKey);
        }
    }

    [Fact]
    public void An_import_takes_the_settings_but_keeps_this_installations_simulator_and_the_unmanaged_lines()
    {
        var installation = WithIni("starter.exe.auto no\nstarter.exe.path eu07_local.exe\ncalibratein 0 0 0 0\nfieldofview 45\n");
        installation.Files.WithFile("/backup/eu07.ini", "starter.exe.auto no\nstarter.exe.path eu07_other.exe\nfieldofview 60\ngfxrenderer legacy\n");

        Assert.True(installation.SettingsStore.ImportFrom("/backup/eu07.ini"));
        var s = installation.SettingsStore.Settings;
        Assert.Equal(60, s.FieldOfView);
        Assert.Equal(SimulatorSettings.RenderEngineOf("legacy"), s.RenderEngine);
        Assert.Equal("eu07_local.exe", s.ExecutablePath);

        // Nothing is written until saved, and the save keeps the user's own lines.
        Assert.Contains("fieldofview 45", installation.Files.ReadAllText(UserIni));
        installation.SettingsStore.Save();
        string saved = installation.Files.ReadAllText(UserIni);
        Assert.Contains("calibratein 0 0 0 0", saved);
        Assert.Contains("fieldofview 60", saved);
        Assert.Contains("starter.exe.path eu07_local.exe", saved);
    }

    [Fact]
    public void An_import_of_a_missing_file_changes_nothing()
    {
        var installation = WithIni("fieldofview 50\n");

        Assert.False(installation.SettingsStore.ImportFrom("/nowhere/eu07.ini"));
        Assert.Equal(50, installation.SettingsStore.Settings.FieldOfView);
    }

    [Fact]
    public void An_export_writes_a_complete_file_elsewhere_and_leaves_the_users_one_alone()
    {
        var installation = WithIni("calibratein 0 0 0 0\nfieldofview 45\n");
        installation.SettingsStore.Settings.FieldOfView = 60;

        Assert.True(installation.SettingsStore.ExportTo("/export/eu07.ini"));

        string exported = installation.Files.ReadAllText("/export/eu07.ini");
        Assert.Contains("calibratein 0 0 0 0", exported);
        Assert.Contains("fieldofview 60", exported);
        Assert.Contains("fieldofview 45", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void An_export_keeps_the_older_renderer_word_of_the_file()
    {
        var installation = WithIni("gfxrenderer legacy\n");

        Assert.True(installation.SettingsStore.ExportTo("/export/eu07.ini"));

        Assert.Contains("gfxrenderer legacy", installation.Files.ReadAllText("/export/eu07.ini"));
    }

    [Fact]
    public void The_gamepad_is_ignored_unless_the_file_turns_it_on()
    {
        Assert.True(Read("").IgnoreGamepad);
        Assert.Equal("input.gamepad no", Line(Write(Read("")), "input.gamepad"));
        Assert.False(Read("input.gamepad yes\n").IgnoreGamepad);
        Assert.True(Read("input.gamepad no\n").IgnoreGamepad);
    }

    [Fact]
    public void The_standard_mode_is_where_a_player_starts_and_the_choice_is_kept()
    {
        Assert.False(Read("").AdvancedSettings);
        Assert.True(Read("starter.settings.advanced yes\n").AdvancedSettings);

        var s = Read("");
        s.AdvancedSettings = true;
        Assert.Equal("starter.settings.advanced yes", Line(Write(s), "starter.settings.advanced"));
    }

    [Fact]
    public void The_move_to_the_standard_mode_is_marked_in_the_file()
    {
        Assert.False(Read("").MovedToStandard);
        Assert.True(Read("starter.settings.standard yes\n").MovedToStandard);

        var s = Read("");
        s.MovedToStandard = true;
        Assert.Equal("starter.settings.standard yes", Line(Write(s), "starter.settings.standard"));
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
