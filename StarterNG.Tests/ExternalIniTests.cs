using StarterNG.Classes;
using StarterNG.Domain.Settings;
using StarterNG.Infrastructure.Settings;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

/// <summary>eu07.ini as other hands leave it: edited while the starter runs, and with lines the starter does not know.</summary>
public class ExternalIniTests
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

    [Fact]
    public void A_key_inside_a_block_comment_is_not_read()
    {
        Assert.False(Read("/*\nfullscreen yes\n*/\n").Fullscreen);
        Assert.False(Read("/* fullscreen yes */\n").Fullscreen);
    }

    [Fact]
    public void A_comment_glued_to_a_value_is_not_part_of_it()
    {
        Assert.True(Read("fullscreen yes//na pelnym ekranie\n").Fullscreen);
    }

    [Fact]
    public void A_save_keeps_what_was_changed_on_disk_meanwhile()
    {
        var installation = WithIni("fieldofview 45\n");
        Edited(installation, "fieldofview 45\nvsync yes\ncalibratein 1 2 3 4\n");

        installation.SettingsStore.Settings.FieldOfView = 60;
        installation.SettingsStore.Save();

        string saved = installation.Files.ReadAllText(UserIni);
        Assert.Contains("fieldofview 60", saved);
        Assert.Contains("calibratein 1 2 3 4", saved);
        Assert.Contains("vsync yes", saved);
        // What is held is the file again: a later save does not take the other change back.
        Assert.True(installation.SettingsStore.Settings.VSync);
        Assert.True(installation.SettingsStore.LastSaveMerged);
    }

    [Fact]
    public void A_key_changed_both_here_and_on_disk_takes_the_players_value()
    {
        var installation = WithIni("fieldofview 45\n");
        Edited(installation, "fieldofview 70\n");

        installation.SettingsStore.Settings.FieldOfView = 60;
        installation.SettingsStore.Save();

        Assert.Contains("fieldofview 60", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void A_renderer_written_in_by_hand_while_the_starter_runs_survives_its_save()
    {
        // Notepad: "gfxrenderer vulkan", a word the starter does not offer.
        var installation = WithIni("gfxrenderer default\nfieldofview 45\n");
        Edited(installation, "gfxrenderer vulkan\nfieldofview 45\n");

        installation.SettingsStore.Settings.FieldOfView = 60;
        installation.SettingsStore.Save();

        string saved = installation.Files.ReadAllText(UserIni);
        Assert.Contains("gfxrenderer vulkan", saved);
        Assert.Contains("fieldofview 60", saved);
        Assert.Equal("vulkan", installation.SettingsStore.Settings.RendererWord);
    }

    [Fact]
    public void A_renderer_written_in_by_hand_and_read_again_survives_the_next_save()
    {
        var installation = WithIni("gfxrenderer default\n");
        Edited(installation, "gfxrenderer vulkan\n");

        // "Wczytać ponownie?" - yes.
        installation.SettingsStore.Load();
        installation.SettingsStore.Save();

        Assert.Contains("gfxrenderer vulkan", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void A_change_on_disk_not_read_again_is_not_lost_on_the_next_save()
    {
        var installation = WithIni("gfxrenderer default\nfieldofview 45\nvsync no\n");
        var store = installation.SettingsStore;
        Edited(installation, "gfxrenderer vulkan\nfieldofview 45\nvsync yes\n");
        Assert.True(store.HasChangeOnDiskToAskAbout());

        // "Wczytać ponownie?" - no: not asked again about the same change.
        store.LeaveChangeOnDisk();
        Assert.False(store.HasChangeOnDiskToAskAbout());

        store.Settings.FieldOfView = 60;
        store.Save();

        string saved = installation.Files.ReadAllText(UserIni);
        Assert.Contains("gfxrenderer vulkan", saved);
        Assert.Contains("vsync yes", saved);
        Assert.Contains("fieldofview 60", saved);
        Assert.True(store.LastSaveMerged);
    }

    [Fact]
    public void A_later_change_on_disk_is_asked_about_again()
    {
        var installation = WithIni("fieldofview 45\n");
        var store = installation.SettingsStore;
        Edited(installation, "fieldofview 50\n");
        store.LeaveChangeOnDisk();

        installation.Files.WithFile(UserIni, "fieldofview 55\n")
                    .WrittenAt(UserIni, installation.Files.GetLastWriteTimeUtc(UserIni).AddMinutes(1));

        Assert.True(store.HasChangeOnDiskToAskAbout());
    }

    [Fact]
    public void A_file_nobody_else_touched_saves_as_before()
    {
        var installation = WithIni("fieldofview 45\n");

        installation.SettingsStore.Settings.FieldOfView = 60;
        installation.SettingsStore.Save();

        Assert.False(installation.SettingsStore.LastSaveMerged);
        Assert.Contains("fieldofview 60", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void A_known_key_with_a_value_the_starter_cannot_read_is_named_with_that_value()
    {
        var installation = WithIni("fieldofview abc\nfullscreen moze\nui.scale duzo\nvsync tak\nwidth 1920.0\n" +
                                   "gfxrenderer legacy\nwidth 1920\ngfx.framebuffer.width 960\ncalibratein 1 2\n");

        var unreadable = installation.SettingsStore.UnreadableValues();

        Assert.Equal("abc", unreadable["fieldofview"]);
        Assert.Equal("moze", unreadable["fullscreen"]);
        Assert.Equal("duzo", unreadable["ui.scale"]);
        // Read another way, derived from other keys, or not the starter's: none of them unknown.
        Assert.False(unreadable.ContainsKey("vsync"));
        Assert.False(unreadable.ContainsKey("width"));
        Assert.False(unreadable.ContainsKey("gfxrenderer"));
        Assert.False(unreadable.ContainsKey("gfx.framebuffer.width"));
        Assert.False(unreadable.ContainsKey("calibratein"));
        // The default features in another order: the same line.
        Assert.False(WithIni("uartfeature local|main|scnd|train\n").SettingsStore.UnreadableValues().ContainsKey("uartfeature"));
        // The file keeps what it says until the setting is changed.
        installation.SettingsStore.Save();
        Assert.Contains("fieldofview abc", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void Values_above_what_the_old_starter_gave_are_brought_down_on_the_next_save()
    {
        var installation = WithIni("maxcabtexturesize 8192\nshadowtune 4096 250 600 300\n" +
                                   "width 1920\nheight 1080\ngfx.framebuffer.width 2880\ngfx.framebuffer.height 1620\n" +
                                   "fieldofview 50\n");
        var store = installation.SettingsStore;

        Assert.Equal(GraphicsRules.MaxCabTextureSize, store.Settings.MaxCabTextureSize);
        Assert.Equal(GraphicsRules.MaxShadowRange, store.Settings.ShadowProjectionRange);
        Assert.Equal(GraphicsRules.MaxBufferScale, store.Settings.BufferScalePercent);
        // Read, only brought down: not a value the starter cannot read.
        Assert.Empty(store.UnreadableValues());

        store.Save();

        string saved = installation.Files.ReadAllText(UserIni);
        Assert.Contains("maxcabtexturesize 4096", saved);
        Assert.Contains("shadowtune 4096 250 400 300", saved);
        Assert.Contains("gfx.framebuffer.width 1920", saved);
        Assert.Contains("gfx.framebuffer.height 1080", saved);
        Assert.Contains("fieldofview 50", saved);
    }

    [Fact]
    public void Values_within_the_caps_are_left_as_the_file_has_them()
    {
        var installation = WithIni("maxcabtexturesize 2048\nshadowtune 2048 250 300 300\n");

        installation.SettingsStore.Save();

        string saved = installation.Files.ReadAllText(UserIni);
        Assert.Contains("maxcabtexturesize 2048", saved);
        Assert.Contains("shadowtune 2048 250 300 300", saved);
    }

    // Another program writes the file a minute after the starter read it.
    private static void Edited(TestInstallation installation, string text) =>
        installation.Files.WithFile(UserIni, text)
                    .WrittenAt(UserIni, installation.Files.GetLastWriteTimeUtc(UserIni).AddMinutes(1));

    [Fact]
    public void Lines_the_starter_does_not_know_survive_a_save_in_place()
    {
        const string ini = "// moj komentarz\nfoo bar baz\n\nfullscreen no\nzmq.address tcp://*:5555\n";
        var installation = WithIni(ini);
        installation.SettingsStore.Settings.Fullscreen = true;

        installation.SettingsStore.Save();

        string saved = installation.Files.ReadAllText(UserIni).Replace("\r\n", "\n");
        Assert.StartsWith("// moj komentarz\nfoo bar baz\n\nfullscreen yes\nzmq.address tcp://*:5555\n", saved);
    }
}
