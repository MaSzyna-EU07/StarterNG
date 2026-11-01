using StarterNG.Domain.Settings;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class SettingsStoreTests
{
    // Joined the way SettingsPaths joins it, so it matches on a Windows host too.
    private static readonly string UserIni = Path.Combine("/home/kolejarz", ".config", "MaSzyna", "eu07.ini");

    private static TestInstallation WithHome(InMemoryFileSystem? files = null)
    {
        var installation = new TestInstallation(files);
        installation.Environment.With("HOME", "/home/kolejarz");
        return installation;
    }

    /// <summary>
    /// The saved file with each of its lines ended by \n, whichever ending the host
    /// writes, so a check for "key value\n" holds the line to exactly that.
    /// </summary>
    private static string SavedLines(TestInstallation installation) =>
        installation.Files.ReadAllText(UserIni).Replace("\r\n", "\n") + "\n";

    /// <summary>What the file says for a key, its value included; null when no line sets it.</summary>
    private static string? LineFor(TestInstallation installation, string key) =>
        SavedLines(installation).Split('\n')
            .SingleOrDefault(line => line == key || line.StartsWith(key + " "));

    /// <summary>
    /// Simulator keys left with no value. The simulator would take the next word of
    /// the file for it; a starter.* key is exempt, as it skips those.
    /// </summary>
    private static string[] BareSimulatorKeys(TestInstallation installation) =>
        SavedLines(installation).Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("//") && !line.Any(char.IsWhiteSpace))
            .Where(key => !key.StartsWith("starter.", StringComparison.OrdinalIgnoreCase) &&
                          !key.Equals("endconfig", StringComparison.OrdinalIgnoreCase))
            .ToArray();

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
        Assert.Equal("pl", settings.LanguageCode);
        Assert.True(settings.LanguageWasSet);
        Assert.Equal(UserIni, installation.SettingsStore.LoadedFrom);
    }

    [Fact]
    public void A_language_the_starter_does_not_ship_survives_a_round_trip()
    {
        // A user-supplied translation must reach the simulator as written; folding
        // every unknown code into "en" is what forced eu07.exe into English.
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "lang zh\n"));
        installation.SettingsStore.Load();

        Assert.Equal("zh", installation.SettingsStore.Settings.LanguageCode);

        installation.SettingsStore.Save();

        Assert.Contains("lang zh", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void A_language_chosen_in_the_picker_is_written_as_its_own_code()
    {
        var installation = WithHome();
        installation.SettingsStore.Load();
        installation.SettingsStore.Settings.Language = "中文";
        installation.SettingsStore.Settings.LanguageCode = "zh";

        installation.SettingsStore.Save();

        Assert.Contains("lang zh", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void Uart_features_with_nothing_selected_are_written_out_in_full()
    {
        var installation = WithHome();
        installation.SettingsStore.Load();
        var settings = installation.SettingsStore.Settings;
        settings.UartMain = settings.UartScnd = settings.UartTrain = settings.UartLocal = false;

        installation.SettingsStore.Save();

        var lines = installation.Files.ReadAllText(UserIni).Split('\n').Select(l => l.Trim());
        Assert.Contains("uartfeature no no no no", lines);
        Assert.DoesNotContain("uartfeature", lines);
    }

    [Theory]
    [InlineData("uartfeature\nuartdebug no\n", false, false)]
    [InlineData("uartfeature none\n", false, false)]
    [InlineData("uartfeature no no no no\n", false, false)]
    [InlineData("uartfeature yes yes yes yes\n", true, true)]
    [InlineData("uartfeature YES no no no\n", true, false)]
    [InlineData("uartfeature main|scnd|\n", true, true)]
    public void Uart_features_are_read_in_every_form_the_simulator_takes(string file, bool main, bool scnd)
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, file));

        installation.SettingsStore.Load();

        Assert.Equal(main, installation.SettingsStore.Settings.UartMain);
        Assert.Equal(scnd, installation.SettingsStore.Settings.UartScnd);
    }

    [Theory]
    [InlineData("")]
    [InlineData("uartdebug no")]
    [InlineData("1 2 3")]
    public void A_uart_tune_that_is_not_eighteen_numbers_is_not_written(string tune)
    {
        var installation = WithHome();
        installation.SettingsStore.Load();
        installation.SettingsStore.Settings.UartTune = tune;

        installation.SettingsStore.Save();

        Assert.DoesNotContain(installation.Files.ReadAllText(UserIni).Split('\n'),
                              line => line.TrimStart().StartsWith("uarttune", StringComparison.Ordinal));
    }

    [Fact]
    public void With_no_language_recorded_the_file_still_names_one()
    {
        var installation = WithHome();
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        Assert.Contains("lang en", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void The_language_code_is_written_in_lower_case()
    {
        var installation = WithHome();
        installation.SettingsStore.Load();
        installation.SettingsStore.Settings.LanguageCode = "ZH";

        installation.SettingsStore.Save();

        Assert.Contains("lang zh", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void The_consist_state_override_keeps_the_key_the_old_starter_used()
    {
        var installation = WithHome(new InMemoryFileSystem()
            .WithFile(UserIni, "starter.batterydefault 2\n"));

        installation.SettingsStore.Load();
        Assert.Equal(ConsistReadyOverride.AlwaysReady, installation.SettingsStore.Settings.ReadyOverride);

        installation.SettingsStore.Settings.ReadyOverride = ConsistReadyOverride.AlwaysCold;
        installation.SettingsStore.Save();

        Assert.Contains("starter.batterydefault 1", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void An_out_of_range_override_falls_back_to_leaving_the_scenery_alone()
    {
        var installation = WithHome(new InMemoryFileSystem()
            .WithFile(UserIni, "starter.batterydefault 9\n"));

        installation.SettingsStore.Load();

        Assert.Equal(ConsistReadyOverride.Scenery, installation.SettingsStore.Settings.ReadyOverride);
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

    [Theory]
    [InlineData("uarttune uartdebug no\n", "")]
    [InlineData("uarttune 1 2 3\n", "1 2 3")]
    public void A_calibration_is_shown_only_if_the_file_holds_numbers_for_it(string line, string shown)
    {
        // The next key of the file on the same line is no calibration, but a wrong count of numbers is one to fix.
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, line));

        installation.SettingsStore.Load();

        Assert.Equal(shown, installation.SettingsStore.Settings.UartTune);
    }

    [Theory]
    [InlineData("uarttune COM3\n", null)]
    [InlineData("", null)]
    [InlineData("uarttune 0 65535 0 65535 10 65535 10 65535 10 65535 10 65535 100000 65535 10000 65535 150 65535\n",
                "uarttune 0 65535 0 65535 10 65535 10 65535 10 65535 10 65535 100000 65535 10000 65535 150 65535")]
    public void The_uart_calibration_is_written_only_as_its_eighteen_numbers(string line, string? written)
    {
        // Anything else and the simulator reads the next keys of the file for them:
        // maxcabtexturesize 2048 went so into one.
        var installation = WithHome(new InMemoryFileSystem()
            .WithFile(UserIni, line + "maxcabtexturesize 2048\n"));
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        var lines = installation.Files.ReadAllText(UserIni).Split('\n').Select(l => l.TrimEnd('\r'));
        Assert.Equal(written, lines.SingleOrDefault(l => l.StartsWith("uarttune")));
    }

    [Fact]
    public void The_uart_line_names_port_speed_and_interval()
    {
        var installation = WithHome(new InMemoryFileSystem()
            .WithFile(UserIni, "uart /dev/ttyACM0 115200 0.05\n"));
        installation.SettingsStore.Load();
        var s = installation.SettingsStore.Settings;

        Assert.True(s.UartEnabled);
        Assert.Equal("/dev/ttyACM0", s.UartPort);
        Assert.Equal(115200, s.UartBaud);
        Assert.Equal(0.05, s.UartUpdateTime);

        s.UartPort = "COM3";
        installation.SettingsStore.Save();

        Assert.Contains("uart COM3 115200 0.05", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void A_bare_uart_port_is_completed_rather_than_left_to_swallow_the_next_keys()
    {
        // The simulator reads three words; "uart COM3" alone took the next line's key
        // and value for the speed and interval.
        var installation = WithHome(new InMemoryFileSystem()
            .WithFile(UserIni, "uart COM3\nmaxcabtexturesize 2048\n"));
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        var text = installation.Files.ReadAllText(UserIni);
        Assert.Contains($"uart COM3 {Uart.DefaultBaud} 0.1", text);
        Assert.Contains("maxcabtexturesize 2048", text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("COM 3")]
    public void Without_a_one_word_port_no_uart_line_is_written(string port)
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "uart COM3 57600 0.1\n"));
        installation.SettingsStore.Load();
        installation.SettingsStore.Settings.UartPort = port;

        installation.SettingsStore.Save();

        var lines = installation.Files.ReadAllText(UserIni).Split('\n');
        Assert.DoesNotContain(lines, l => l.StartsWith("uart "));
    }

    [Fact]
    public void A_disabled_uart_leaves_no_uart_line()
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "uart COM3 57600 0.1\n"));
        installation.SettingsStore.Load();
        installation.SettingsStore.Settings.UartEnabled = false;

        installation.SettingsStore.Save();

        var lines = installation.Files.ReadAllText(UserIni).Split('\n');
        Assert.DoesNotContain(lines, l => l.StartsWith("uart "));
    }

    [Fact]
    public void The_graphics_card_choice_survives_a_round_trip()
    {
        var installation = WithHome();
        installation.SettingsStore.Load();
        Assert.Equal(GpuPreference.Discrete, installation.SettingsStore.Settings.Gpu);

        installation.SettingsStore.Settings.Gpu = GpuPreference.System;
        installation.SettingsStore.Save();

        Assert.Contains("starter.gpu system", installation.Files.ReadAllText(UserIni));
        var reopened = WithHome(installation.Files);
        reopened.SettingsStore.Load();
        Assert.Equal(GpuPreference.System, reopened.SettingsStore.Settings.Gpu);
    }

    [Fact]
    public void A_silenced_processor_notice_stays_silent()
    {
        var installation = WithHome();
        installation.SettingsStore.Load();
        Assert.True(installation.SettingsStore.Settings.CpuNotice);

        installation.SettingsStore.Settings.CpuNotice = false;
        installation.SettingsStore.Save();

        var reopened = WithHome(installation.Files);
        reopened.SettingsStore.Load();
        Assert.False(reopened.SettingsStore.Settings.CpuNotice);
    }

    [Fact]
    public void Uart_features_all_off_are_written_as_four_noes()
    {
        // "none" only newer simulators read; every build reads the four words.
        var installation = WithHome();
        installation.SettingsStore.Load();
        var s = installation.SettingsStore.Settings;
        s.UartMain = s.UartScnd = s.UartTrain = s.UartLocal = s.UartRadioVolume = s.UartRadioChannel = false;

        installation.SettingsStore.Save();

        Assert.Equal("uartfeature no no no no", LineFor(installation, "uartfeature"));
    }

    [Fact]
    public void Dynamic_brake_alone_is_written_in_the_pipe_form()
    {
        // The four words cannot say it, and would switch it off.
        var installation = WithHome();
        installation.SettingsStore.Load();
        var s = installation.SettingsStore.Settings;
        s.UartMain = s.UartScnd = s.UartTrain = s.UartLocal = false;
        s.UartDynamicBrake = true;

        installation.SettingsStore.Save();

        Assert.Equal("uartfeature dynamicbrake|", LineFor(installation, "uartfeature"));
    }

    [Theory]
    // Globals.cpp: a first word with a pipe, "none" or a feature's name is the list.
    [InlineData("main|scnd|train|local|radiovolume|radiochannel|dynamicbrake", "main scnd train local radiovolume radiochannel dynamicbrake")]
    [InlineData("main|dynamicbrake|", "main dynamicbrake")]
    [InlineData("MAIN|Train", "main train")]
    [InlineData("main", "main")]
    [InlineData("dynamicbrake", "dynamicbrake")]
    [InlineData("nonsense|local", "local")]
    [InlineData("none", "")]
    [InlineData("NONE", "")]
    // Anything else four words, for the first four features: parser.cpp takes true, yes and 1.
    [InlineData("yes yes yes yes", "main scnd train local")]
    [InlineData("no no no no", "")]
    [InlineData("YES no Yes NO", "main train")]
    [InlineData("1 0 1 0", "main train")]
    [InlineData("true false false true", "main local")]
    // Bare: nothing selected, whatever the next line says.
    [InlineData("", "")]
    public void A_uartfeature_line_is_read_as_the_simulator_reads_it(string value, string features)
    {
        var installation = WithHome(new InMemoryFileSystem()
            .WithFile(UserIni, $"uartfeature {value}\nwidth 800\n"));

        installation.SettingsStore.Load();

        var s = installation.SettingsStore.Settings;
        var on = new List<string>();
        if (s.UartMain) on.Add("main");
        if (s.UartScnd) on.Add("scnd");
        if (s.UartTrain) on.Add("train");
        if (s.UartLocal) on.Add("local");
        if (s.UartRadioVolume) on.Add("radiovolume");
        if (s.UartRadioChannel) on.Add("radiochannel");
        if (s.UartDynamicBrake) on.Add("dynamicbrake");
        Assert.Equal(features, string.Join(' ', on));
        Assert.Equal(800, s.Width);
    }

    [Theory]
    [InlineData("main|scnd|train|local|radiovolume|radiochannel|dynamicbrake")]
    [InlineData("main|dynamicbrake")]
    [InlineData("dynamicbrake")]
    [InlineData("yes yes yes yes")]
    [InlineData("no yes no YES")]
    [InlineData("no no no no")]
    public void A_uartfeature_line_nobody_changed_stays_as_the_file_has_it(string value)
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, $"uartfeature {value}\n"));
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        Assert.Equal($"uartfeature {value}", LineFor(installation, "uartfeature"));
    }

    [Theory]
    [InlineData("uartfeature none", "uartfeature no no no no")]
    [InlineData("uartfeature", "uartfeature no no no no")]
    // Short of its four words, the simulator would read the next key for the rest.
    [InlineData("uartfeature yes yes", "uartfeature main|scnd|")]
    [InlineData("uartfeature nonsense", "uartfeature no no no no")]
    public void A_uartfeature_line_not_every_simulator_reads_whole_is_put_right(string line, string written)
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, line + "\nmaxcabtexturesize 2048\n"));
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        Assert.Equal(written, LineFor(installation, "uartfeature"));
        Assert.Equal("maxcabtexturesize 2048", LineFor(installation, "maxcabtexturesize"));
    }

    [Theory]
    [InlineData("yes yes yes yes", "uartfeature scnd|train|local|")]
    [InlineData("main|dynamicbrake", "uartfeature dynamicbrake|")]
    public void A_uart_feature_taken_off_rewrites_the_line_in_the_pipe_form(string value, string written)
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, $"uartfeature {value}\n"));
        installation.SettingsStore.Load();
        installation.SettingsStore.Settings.UartMain = false;

        installation.SettingsStore.Save();

        Assert.Equal(written, LineFor(installation, "uartfeature"));
    }

    [Fact]
    public void The_uart_interval_is_shown_within_what_its_box_holds()
    {
        Assert.Equal(0.05m, Uart.ShownUpdateTime(0.05));
        // 1e30 is more than a decimal can hold: the window died on opening.
        Assert.Equal(10m, Uart.ShownUpdateTime(1e30));
        Assert.Equal(10m, Uart.ShownUpdateTime(double.PositiveInfinity));
        Assert.Equal((decimal)Uart.DefaultUpdateTime, Uart.ShownUpdateTime(double.NaN));
        Assert.Equal(0m, Uart.ShownUpdateTime(-1));
    }

    [Theory]
    [InlineData("mousescale 1.5 0.5", false, true, "mousescale 1.5 -0.5")]
    [InlineData("mousescale 1.5 0.5", true, false, "mousescale -1.5 0.5")]
    [InlineData("mousescale -1.5 -0.5", false, false, "mousescale 1.5 0.5")]
    [InlineData("mousescale 2.5 0.123456", false, true, "mousescale 2.5 -0.123456")]
    // Further than the slider goes.
    [InlineData("mousescale 12 0.5", true, false, "mousescale -12 0.5")]
    public void A_mouse_scale_keeps_its_numbers_when_an_axis_is_inverted(
        string line, bool invertX, bool invertY, string written)
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, line + "\n"));
        installation.SettingsStore.Load();
        installation.SettingsStore.Settings.InvertMouseHorizontal = invertX;
        installation.SettingsStore.Settings.InvertMouseVertical = invertY;

        installation.SettingsStore.Save();

        Assert.Equal(written, LineFor(installation, "mousescale"));
    }

    [Fact]
    public void A_mouse_scale_nobody_changed_stays_as_the_file_has_it()
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "mousescale 1.5 0.5\n"));
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        Assert.Equal("mousescale 1.5 0.5", LineFor(installation, "mousescale"));
    }

    [Fact]
    public void Moving_the_sensitivity_replaces_the_scale_it_stood_for()
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "mousescale 1.5 0.5\n"));
        installation.SettingsStore.Load();
        Assert.Equal(2, installation.SettingsStore.Settings.CursorSensitivity);
        installation.SettingsStore.Settings.CursorSensitivity = 3;

        installation.SettingsStore.Save();

        Assert.Equal("mousescale 3 0.5", LineFor(installation, "mousescale"));
    }

    [Fact]
    public void The_default_mouse_scale_is_written_as_before()
    {
        var installation = WithHome();
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        Assert.Equal("mousescale 2 0.2", LineFor(installation, "mousescale"));
    }

    // ── A file that could not be read ────────────────────────────────────────

    [Fact]
    public void A_file_that_could_not_be_read_is_not_saved_over()
    {
        const string held = "width 1920\nsomethingexotic 42\n";
        var files = new InMemoryFileSystem().WithFile(UserIni, held).FailReading(UserIni);
        var installation = WithHome(files);
        installation.SettingsStore.Load();
        Assert.True(installation.SettingsStore.LoadFailed);
        Assert.Equal(1280, installation.SettingsStore.Settings.Width);

        installation.SettingsStore.Save();
        installation.SettingsStore.CaptureAndSave();

        files.AllowReading(UserIni);
        Assert.Equal(held, files.ReadAllText(UserIni));
        Assert.Contains("not saved", installation.Log.Text);
        // The screen must not say "saved" over this.
        Assert.True(installation.SettingsStore.LastSaveFailed);

        installation.SettingsStore.Load();
        installation.SettingsStore.Save();
        Assert.False(installation.SettingsStore.LastSaveFailed);
    }

    [Fact]
    public void A_profile_of_the_settings_is_not_written_from_defaults_either()
    {
        const string profile = "/home/kolejarz/profile.ini";
        var files = new InMemoryFileSystem().WithFile(UserIni, "width 1920\n").FailReading(UserIni);
        var installation = WithHome(files);
        installation.SettingsStore.Load();

        installation.SettingsStore.SaveTo(profile);

        Assert.False(files.FileExists(profile));
    }

    [Fact]
    public void A_read_that_works_again_lets_the_settings_be_saved()
    {
        var files = new InMemoryFileSystem().WithFile(UserIni, "width 1920\n").FailReading(UserIni);
        var installation = WithHome(files);
        installation.SettingsStore.Load();

        files.AllowReading(UserIni);
        installation.SettingsStore.Load();
        Assert.False(installation.SettingsStore.LoadFailed);
        installation.SettingsStore.Settings.Height = 1080;
        installation.SettingsStore.Save();

        Assert.Equal("width 1920", LineFor(installation, "width"));
        Assert.Equal("height 1080", LineFor(installation, "height"));
    }

    [Fact]
    public void A_shipped_ini_that_could_not_be_read_leaves_no_file_of_defaults_in_its_way()
    {
        // The user's own file, once there, is the one read from then on.
        var files = new InMemoryFileSystem()
            .WithFile(TestInstallation.At("eu07.ini"), "width 800\n")
            .FailReading(TestInstallation.At("eu07.ini"));
        var installation = WithHome(files);
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        Assert.False(files.FileExists(UserIni));
    }

    // ── endconfig ────────────────────────────────────────────────────────────

    [Fact]
    public void A_key_the_file_lacks_goes_before_endconfig()
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "width 1920\nendconfig\n"));
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        var lines = SavedLines(installation).Split('\n').ToList();
        int end = lines.IndexOf("endconfig");
        Assert.True(end > lines.IndexOf("fullscreen no") && lines.IndexOf("fullscreen no") >= 0);
        Assert.DoesNotContain(lines.Skip(end + 1), line => line.Trim().Length > 0);
    }

    [Fact]
    public void A_key_an_older_save_left_past_endconfig_moves_before_it()
    {
        // Set there by this starter's own earlier save, and never read by the simulator.
        var installation = WithHome(new InMemoryFileSystem()
            .WithFile(UserIni, "width 1920\nendconfig\nfieldofview 60\nnotes about this file\n"));
        var store = installation.SettingsStore;
        store.Load();
        Assert.Equal(60, store.Settings.FieldOfView);
        Assert.Contains(new StarterNG.Application.IniChange("fieldofview", null, "60"), store.Changes(store.Settings));

        store.Save();

        var lines = SavedLines(installation).Split('\n').ToList();
        Assert.True(lines.IndexOf("fieldofview 60") < lines.IndexOf("endconfig"));
        Assert.Single(lines, "fieldofview 60");
        // What is no key of the starter's stays where the simulator does not read it.
        Assert.True(lines.IndexOf("notes about this file") > lines.IndexOf("endconfig"));
    }

    [Fact]
    public void A_disabled_uart_past_endconfig_is_taken_out_rather_than_moved()
    {
        var installation = WithHome(new InMemoryFileSystem()
            .WithFile(UserIni, "endconfig\nuart COM3 57600 0.1\n"));
        installation.SettingsStore.Load();
        installation.SettingsStore.Settings.UartEnabled = false;

        installation.SettingsStore.Save();

        var lines = installation.Files.ReadAllText(UserIni).Replace("\r\n", "\n").Split('\n');
        Assert.DoesNotContain(lines, l => l.StartsWith("uart "));
    }

    // ── No bare keys ─────────────────────────────────────────────────────────

    [Fact]
    public void Saving_the_default_settings_leaves_no_simulator_key_bare()
    {
        // A bare key takes the next word of the file for its value: uartfeature the
        // next four, which is how keys after it went unread.
        var installation = WithHome();
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        Assert.Empty(BareSimulatorKeys(installation));
    }

    [Fact]
    public void Everything_emptied_leaves_no_simulator_key_bare()
    {
        var installation = WithHome();
        installation.SettingsStore.Load();
        var s = installation.SettingsStore.Settings;
        s.LanguageCode = "   ";
        s.UartEnabled = true;
        s.UartPort = "  ";
        s.UartTune = "  ";
        s.UartMain = s.UartScnd = s.UartTrain = s.UartLocal = s.UartRadioVolume = s.UartRadioChannel = false;
        s.ExecutablePath = "";
        s.LastScenery = "";
        foreach (var key in SimulatorKeys.All)
            s.Extra[key.Key] = " ";

        installation.SettingsStore.Save();

        Assert.Empty(BareSimulatorKeys(installation));
    }

    [Theory]
    [InlineData("uart")]
    [InlineData("uarttune")]
    [InlineData("uartfeature")]
    [InlineData("mousescale")]
    [InlineData("fullscreen")]
    [InlineData("width")]
    [InlineData("screenshotsdir")]
    [InlineData("fpslimit")]
    [InlineData("gfx.angleplatform")]
    public void A_key_the_file_itself_leaves_bare_does_not_stay_so(string key)
    {
        var installation = WithHome(new InMemoryFileSystem()
            .WithFile(UserIni, $"{key}\nmaxcabtexturesize 2048\n"));
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        Assert.Empty(BareSimulatorKeys(installation));
        Assert.Equal("maxcabtexturesize 2048", LineFor(installation, "maxcabtexturesize"));
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

    [Theory]
    [InlineData("144")]
    [InlineData("360")]
    [InlineData("59.94")]
    public void A_frame_rate_limit_the_slider_once_cut_to_100_survives_a_save(string limit)
    {
        // The simulator takes any rate above zero.
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, $"fpslimit {limit}\n"));
        installation.SettingsStore.Load();
        Assert.True(installation.SettingsStore.Settings.FpsLimitEnabled);

        installation.SettingsStore.Save();

        Assert.Contains($"fpslimit {limit}", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void A_frame_rate_limit_of_zero_is_no_limit()
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "fpslimit 0\n"));
        installation.SettingsStore.Load();

        Assert.False(installation.SettingsStore.Settings.FpsLimitEnabled);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0.5")]
    [InlineData("60")]
    public void A_reflection_rate_below_the_old_slider_survives_a_save(string rate)
    {
        // The game's own eu07.ini has 1; the slider's floor of 5 raised it on any save.
        var installation = WithHome(new InMemoryFileSystem()
            .WithFile(UserIni, $"gfx.reflections.framerate {rate}\n"));
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        Assert.Contains($"gfx.reflections.framerate {rate}\n", SavedLines(installation));
    }

    [Fact]
    public void Without_a_reflection_rate_the_simulators_own_is_written()
    {
        var installation = WithHome();
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        Assert.Contains("gfx.reflections.framerate 0.003333", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void The_shadow_range_is_the_third_number_of_shadowtune()
    {
        // map size, unread, projection range, unread - as the game's eu07.ini says.
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "shadowtune 1024 100 150 300\n"));
        installation.SettingsStore.Load();
        Assert.Equal(1024, installation.SettingsStore.Settings.ShadowMapResolution);
        Assert.Equal(150, installation.SettingsStore.Settings.ShadowProjectionRange);

        installation.SettingsStore.Settings.ShadowProjectionRange = 200;
        installation.SettingsStore.Save();

        Assert.Contains("shadowtune 1024 100 200 300", installation.Files.ReadAllText(UserIni));
    }

    [Theory]
    [InlineData(1024, 75)]
    [InlineData(2048, 75)]
    [InlineData(4096, 150)]
    [InlineData(8192, 300)]
    public void The_shadow_range_floor_follows_the_map_size(int mapSize, int floor) =>
        Assert.Equal(floor, SimulatorSettings.ShadowRangeFloor(mapSize));

    [Theory]
    [InlineData(150, 1024, false, 150)]
    [InlineData(150, 4096, false, 300)]
    [InlineData(150, 4096, true, 150)]
    [InlineData(100, 8192, false, 1200)]
    [InlineData(400, 8192, false, 1600)]
    public void Shadows_reach_as_far_as_the_renderer_stretches_them(int range, int mapSize, bool legacy, int reach) =>
        Assert.Equal(reach, SimulatorSettings.ShadowReach(range, mapSize, legacy));

    [Theory]
    [InlineData("anisotropicfiltering 12")]
    [InlineData("multisampling 4")]
    public void A_value_the_slider_does_not_offer_survives_a_save(string line)
    {
        // x16 multisampling and an odd anisotropy are the simulator's to take.
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, line + "\n"));
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        Assert.Contains(line, installation.Files.ReadAllText(UserIni));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("simpleshader")]
    [InlineData("off")]
    public void A_renderer_word_the_simulator_reads_as_another_stays_until_changed(string word)
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, $"gfxrenderer {word}\n"));
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        Assert.Contains($"gfxrenderer {word}\n", SavedLines(installation));
    }

    [Theory]
    [InlineData(0, "full")]
    [InlineData(1, "legacy")]
    [InlineData(2, "simple")]
    [InlineData(3, "experimental")]
    public void A_chosen_renderer_is_saved_as_the_simulator_reads_it(int engine, string saved)
    {
        // application.cpp knows full (or default), experimental and simple; every
        // other word - "simpleshader" and "off" from older starters - is legacy.
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "gfxrenderer simpleshader\n"));
        installation.SettingsStore.Load();
        installation.SettingsStore.Settings.RenderEngine = engine == 1 ? 0 : engine;
        installation.SettingsStore.Save();

        installation.SettingsStore.Settings.RenderEngine = engine;
        installation.SettingsStore.Save();

        Assert.Contains($"gfxrenderer {saved}\n", SavedLines(installation));
    }

    [Fact]
    public void A_field_of_view_down_to_ten_degrees_survives_a_save()
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "fieldofview 10\n"));
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        Assert.Contains("fieldofview 10", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void A_silenced_radio_stays_silent()
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "sound.volume.radio 0\n"));
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        Assert.Contains("sound.volume.radio 0\n", SavedLines(installation));
    }

    /// <summary>Stands in for the settings screen holding an unsaved edit.</summary>
    private sealed class WidthCapture : StarterNG.Application.ISettingsCapture
    {
        private readonly int _width;

        public WidthCapture(int width) => _width = width;

        public void CaptureInto(SimulatorSettings settings) => settings.Width = _width;
    }

    [Fact]
    public void A_simulator_key_nobody_touched_stays_out_of_the_file()
    {
        var installation = WithHome();
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        Assert.DoesNotContain("basedrawrange", installation.Files.ReadAllText(UserIni));
        Assert.DoesNotContain("ui.scale", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void A_simulator_key_keeps_its_value_and_comment_through_a_save()
    {
        var installation = WithHome(new InMemoryFileSystem()
            .WithFile(UserIni, "basedrawrange 15000\t// further than the slider goes\n"));
        installation.SettingsStore.Load();

        Assert.Equal("15000", installation.SettingsStore.Settings.Extra["basedrawrange"]);

        installation.SettingsStore.Save();

        Assert.Contains("basedrawrange 15000\t// further than the slider goes",
                        installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void A_changed_simulator_key_is_written()
    {
        var installation = WithHome();
        installation.SettingsStore.Load();
        installation.SettingsStore.Settings.Extra["shakefactor"] =
            SimulatorKeys.All.Single(k => k.Key == "shakefactor").FormatNumber(0.5);

        installation.SettingsStore.Save();

        Assert.Contains("shakefactor 0.5 0.5 0.5", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void An_emptied_word_is_taken_out_rather_than_left_bare()
    {
        // A bare "screenshotsdir" would swallow the next line's key as its value.
        var installation = WithHome(new InMemoryFileSystem()
            .WithFile(UserIni, "screenshotsdir shots\nfullscreen yes\n"));
        installation.SettingsStore.Load();
        installation.SettingsStore.Settings.Extra["screenshotsdir"] = "";

        installation.SettingsStore.Save();

        string text = installation.Files.ReadAllText(UserIni);
        Assert.Contains("// screenshotsdir shots", text);
        Assert.Contains("fullscreen yes", text);
    }

    // ── A save changes only what was changed ─────────────────────────────────

    [Theory]
    [InlineData("fieldofview 80")]
    [InlineData("dynamiclights 9")]
    [InlineData("anisotropicfiltering   16.0\t// as far as it goes")]
    [InlineData("gfx.smoke.fidelity 1.5")]
    public void A_line_nobody_changed_stays_as_the_file_has_it(string line)
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, line + "\n"));
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        Assert.Contains(line + "\n", SavedLines(installation));
    }

    [Fact]
    public void A_changed_value_replaces_the_files()
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "fieldofview 80\n"));
        installation.SettingsStore.Load();
        installation.SettingsStore.Settings.FieldOfView = 50;

        installation.SettingsStore.Save();

        Assert.Contains("fieldofview 50", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void A_key_the_file_lacks_is_written_out()
    {
        // So the simulator and the screen go by the same value.
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "fieldofview 45\n"));
        installation.SettingsStore.Load();

        installation.SettingsStore.Save();

        Assert.Contains("fullscreen no", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void A_save_reports_what_it_would_change_before_making_it()
    {
        var installation = WithHome(new InMemoryFileSystem()
            .WithFile(UserIni, "fullscreen no\nfieldofview 80\ngfx.postfx.tonemapping yes\n"));
        var store = installation.SettingsStore;
        store.Load();
        store.Settings.Fullscreen = true;

        var changes = store.Changes(store.Settings);

        Assert.Contains(new StarterNG.Application.IniChange("fullscreen", "no", "yes"), changes);
        Assert.Contains(new StarterNG.Application.IniChange("gfx.postfx.tonemapping", "yes", null), changes);
        Assert.DoesNotContain(changes, change => change.Key == "fieldofview");
        Assert.Contains("fullscreen no", installation.Files.ReadAllText(UserIni));
    }

    /// <summary>Stands in for a screen whose combo box cannot show the file's shadow rank.</summary>
    private sealed class ShadowRankScreen : StarterNG.Application.ISettingsCapture
    {
        public int Shown = 3;

        public void CaptureInto(SimulatorSettings settings) => settings.ShadowRankCutoff = Shown;

        public IReadOnlyDictionary<string, StarterNG.Application.ShownValue> ShownUnlikeSettings { get; } =
            new Dictionary<string, StarterNG.Application.ShownValue>
            {
                ["gfx.shadow.rank.cutoff"] = new("3", "5")
            };
    }

    [Fact]
    public void A_value_the_screen_could_not_show_survives_until_moved()
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "gfx.shadow.rank.cutoff 5\n"));
        var store = installation.SettingsStore;
        var screen = new ShadowRankScreen();
        store.RegisterCapture(screen);
        store.Load();

        store.CaptureAndSave();
        Assert.Contains("gfx.shadow.rank.cutoff 5", installation.Files.ReadAllText(UserIni));

        screen.Shown = 2;
        store.CaptureAndSave();
        Assert.Contains("gfx.shadow.rank.cutoff 2", installation.Files.ReadAllText(UserIni));

        // Moved back to what it first showed, it is a choice like any other.
        screen.Shown = 3;
        store.CaptureAndSave();
        Assert.Contains("gfx.shadow.rank.cutoff 3", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void A_section_put_back_takes_the_simulators_own_values_for_its_keys_alone()
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni,
            "fieldofview 60\nmaxtexturesize 1024\nshadowtune 4096 250 500 300\nbasedrawrange 4000\n"));
        var store = installation.SettingsStore;
        store.Load();

        var reset = store.WithDefaults(store.Settings, new[] { "fieldofview", "shadowtune", "basedrawrange" });

        Assert.Equal(45, reset.FieldOfView);
        Assert.Equal(2048, reset.ShadowMapResolution);
        Assert.Equal(250, reset.ShadowProjectionRange);
        Assert.False(reset.Extra.ContainsKey("basedrawrange"));
        Assert.Equal(1024, reset.MaxTextureSize);
        // The settings themselves wait for the screen's save.
        Assert.Equal(60, store.Settings.FieldOfView);
    }

    // ── What the screen shows otherwise ──────────────────────────────────────

    [Fact]
    public void Settings_shown_as_they_are_held_have_nothing_unlike()
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "fieldofview 60\nbasedrawrange 4000\n"));
        var store = installation.SettingsStore;
        store.Load();

        Assert.Empty(store.ShownUnlike(store.Settings.Copy()));
    }

    [Fact]
    public void A_value_shown_otherwise_comes_with_both_sides_under_any_spelling_of_its_key()
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "fieldofview 60\n"));
        var store = installation.SettingsStore;
        store.Load();
        var shown = store.Settings.Copy();
        shown.FieldOfView = 45;

        var unlike = store.ShownUnlike(shown);

        Assert.Equal("fieldofview", Assert.Single(unlike).Key);
        Assert.Equal(new StarterNG.Application.ShownValue("45", "60"), unlike["FIELDOFVIEW"]);
    }

    [Fact]
    public void A_key_on_one_side_alone_shows_as_null_on_the_other()
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "basedrawrange 4000\n"));
        var store = installation.SettingsStore;
        store.Load();

        var added = store.Settings.Copy();
        added.Extra["heightbase"] = "2";
        Assert.Equal(new StarterNG.Application.ShownValue("2", null), store.ShownUnlike(added)["heightbase"]);

        var taken = store.Settings.Copy();
        taken.Extra.Remove("basedrawrange");
        Assert.Equal(new StarterNG.Application.ShownValue(null, "4000"), store.ShownUnlike(taken)["basedrawrange"]);
    }

    // ── Applying a profile ───────────────────────────────────────────────────

    private static readonly string Profile = Path.Combine("/home/kolejarz", "profiles", "wieczor.ini");

    [Fact]
    public void A_readable_profile_is_loaded()
    {
        var installation = WithHome(new InMemoryFileSystem()
            .WithFile(UserIni, "fieldofview 60\n").WithFile(Profile, "fieldofview 50\n"));
        var store = installation.SettingsStore;
        store.Load();

        Assert.True(store.TryLoadProfile(Profile));

        Assert.Equal(50, store.Settings.FieldOfView);
        Assert.False(store.LoadFailed);
    }

    [Fact]
    public void A_missing_profile_leaves_the_settings_as_they_are()
    {
        var installation = WithHome(new InMemoryFileSystem().WithFile(UserIni, "fieldofview 60\n"));
        var store = installation.SettingsStore;
        store.Load();
        store.Settings.FieldOfView = 70;

        Assert.False(store.TryLoadProfile(Profile));

        Assert.Equal(70, store.Settings.FieldOfView);
    }

    [Fact]
    public void An_unreadable_profile_leaves_the_real_settings_not_defaults_to_save_over_them()
    {
        var installation = WithHome(new InMemoryFileSystem()
            .WithFile(UserIni, "fieldofview 60\n").WithFile(Profile, "fieldofview 50\n").FailReading(Profile));
        var store = installation.SettingsStore;
        store.Load();

        Assert.False(store.TryLoadProfile(Profile));

        Assert.False(store.LoadFailed);
        Assert.Equal(60, store.Settings.FieldOfView);
        store.Save();
        Assert.False(store.LastSaveFailed);
        Assert.Contains("fieldofview 60", installation.Files.ReadAllText(UserIni));
    }

    [Fact]
    public void A_missing_profile_gives_settings_that_could_not_be_read_another_try()
    {
        var files = new InMemoryFileSystem().WithFile(UserIni, "fieldofview 60\n").FailReading(UserIni);
        var installation = WithHome(files);
        var store = installation.SettingsStore;
        store.Load();
        Assert.True(store.LoadFailed);

        files.AllowReading(UserIni);
        Assert.False(store.TryLoadProfile(Profile));

        Assert.False(store.LoadFailed);
        Assert.Equal(60, store.Settings.FieldOfView);
    }
}
