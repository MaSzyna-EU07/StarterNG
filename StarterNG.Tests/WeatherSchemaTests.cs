using System.Globalization;
using StarterNG.Application;
using StarterNG.Domain.Settings;
using StarterNG.Infrastructure.Settings;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

/// <summary>weather-schema.txt, which the simulator's build writes next to the simulator, read into rows of the settings.</summary>
public class WeatherSchemaTests
{
    private const string Header = "# a comment\nschema\t1\n";

    private static string Toggle(string key, string value = "1", string group = "main", string label = "A switch") =>
        $"bool\t{key}\t{value}\t{group}\t{label}\n";

    private static string Number(string key, string value = "0.75", string minimum = "0.3", string maximum = "1",
                                 string decimals = "2", string group = "adhesion", string label = "A number") =>
        $"float\t{key}\t{value}\t{minimum}\t{maximum}\t{decimals}\t{group}\t{label}\n";

    [Fact]
    public void A_switch_and_a_number_become_rows_of_the_physics_settings()
    {
        var keys = WeatherSchema.Parse(Header +
                                       Toggle("physics.weather", "1", "main", "Weather in the physics") +
                                       Toggle("physics.weather.arcs", "0", "wires", "Arcs") +
                                       Number("physics.weather.wet", "0.75", "0.3", "1", "2", "adhesion", "Adhesion, wet rail"));

        Assert.Equal(3, keys.Count);

        var main = keys[0];
        Assert.Equal("physics.weather", main.Key);
        Assert.Equal(SimulatorKeyKind.Toggle, main.Kind);
        Assert.Equal("yes", main.Default);
        Assert.Equal(SimulatorKeys.Physics, main.Category);
        Assert.Equal("IniSectionWeather.main", main.Section);
        Assert.Equal("Weather in the physics", main.FallbackLabel);

        Assert.Equal("no", keys[1].Default);
        Assert.Equal("IniSectionWeather.wires", keys[1].Section);

        var wet = keys[2];
        Assert.Equal(SimulatorKeyKind.Number, wet.Kind);
        Assert.Equal("0.75", wet.Default);
        Assert.Equal(0.3, wet.Minimum);
        Assert.Equal(1.0, wet.Maximum);
        Assert.Equal(0.01, wet.Step);
        Assert.Equal("Adhesion, wet rail", wet.FallbackLabel);
        Assert.Equal("Ini.physics.weather.wet", wet.LabelKey);
    }

    [Theory]
    [InlineData("0", 1.0)]
    [InlineData("1", 0.1)]
    [InlineData("2", 0.01)]
    [InlineData("3", 0.001)]
    [InlineData("4", 0.0001)]
    public void The_step_of_a_number_is_the_last_decimal_it_is_shown_with(string decimals, double step)
    {
        var key = Assert.Single(WeatherSchema.Parse(Header + Number("physics.weather.wet", decimals: decimals)));

        Assert.Equal(step, key.Step);
    }

    [Theory]
    [InlineData("")]
    [InlineData("# only a comment\n")]
    [InlineData("schema\t2\nbool\tphysics.weather\t1\tmain\tA\n")]
    [InlineData("schema\tx\nbool\tphysics.weather\t1\tmain\tA\n")]
    [InlineData("schema\t-1\nbool\tphysics.weather\t1\tmain\tA\n")]
    [InlineData("bool\tphysics.weather\t1\tmain\tA\n")]
    [InlineData("bool\tphysics.weather\t1\tmain\tA\nschema\t1\n")]
    public void A_file_without_the_schema_this_starter_knows_gives_no_keys(string text)
    {
        Assert.Empty(WeatherSchema.Parse(text));
    }

    [Fact]
    public void No_text_gives_no_keys()
    {
        Assert.Empty(WeatherSchema.Parse(null));
    }

    [Theory]
    // not a key of the weather: the starter writes nothing else of eu07.ini because of this file
    [InlineData("bool\tbasedrawrange\t1\tmain\tA")]
    [InlineData("bool\tphysics\t1\tmain\tA")]
    [InlineData("bool\tphysics.weatherx\t1\tmain\tA")]
    [InlineData("bool\tphysics.weather.\t1\tmain\tA")]
    [InlineData("bool\tphysics.weather..wet\t1\tmain\tA")]
    [InlineData("bool\tphysics.weather.wet.\t1\tmain\tA")]
    [InlineData("bool\tphysics.weather.Wet\t1\tmain\tA")]
    [InlineData("bool\tphysics.weather.wet value\t1\tmain\tA")]
    [InlineData("bool\tphysics.weather.wet/x\t1\tmain\tA")]
    // a switch is 0 or 1
    [InlineData("bool\tphysics.weather.wet\t2\tmain\tA")]
    [InlineData("bool\tphysics.weather.wet\tyes\tmain\tA")]
    [InlineData("bool\tphysics.weather.wet\t\tmain\tA")]
    // a group is a name for a translation key
    [InlineData("bool\tphysics.weather.wet\t1\t\tA")]
    [InlineData("bool\tphysics.weather.wet\t1\tMain\tA")]
    [InlineData("bool\tphysics.weather.wet\t1\tmain group\tA")]
    [InlineData("bool\tphysics.weather.wet\t1\tmain.group\tA")]
    // a line too short, a kind nobody knows
    [InlineData("bool\tphysics.weather.wet\t1\tmain")]
    [InlineData("float\tphysics.weather.wet\t0.5\t0\t1\t2\tadhesion")]
    [InlineData("integer\tphysics.weather.wet\t1\tmain\tA")]
    // a number is a number, with a point, and the range is one
    [InlineData("float\tphysics.weather.wet\tNaN\t0\t1\t2\tadhesion\tA")]
    [InlineData("float\tphysics.weather.wet\t0.5\t-Infinity\t1\t2\tadhesion\tA")]
    [InlineData("float\tphysics.weather.wet\t0.5\t0\tInfinity\t2\tadhesion\tA")]
    [InlineData("float\tphysics.weather.wet\t0,5\t0\t1\t2\tadhesion\tA")]
    [InlineData("float\tphysics.weather.wet\t1 000\t0\t2000\t0\tadhesion\tA")]
    [InlineData("float\tphysics.weather.wet\t0.5\t1\t0\t2\tadhesion\tA")]
    [InlineData("float\tphysics.weather.wet\t0.5\t0.5\t0.5\t2\tadhesion\tA")]
    [InlineData("float\tphysics.weather.wet\t2\t0\t1\t2\tadhesion\tA")]
    [InlineData("float\tphysics.weather.wet\t-1\t0\t1\t2\tadhesion\tA")]
    [InlineData("float\tphysics.weather.wet\t0.5\t0\t1\t-1\tadhesion\tA")]
    [InlineData("float\tphysics.weather.wet\t0.5\t0\t1\t7\tadhesion\tA")]
    [InlineData("float\tphysics.weather.wet\t0.5\t0\t1\ttwo\tadhesion\tA")]
    public void An_entry_that_makes_no_sense_is_left_out(string entry)
    {
        var keys = WeatherSchema.Parse(Header + entry + "\n" + Toggle("physics.weather.arcs", "1", "wires"));

        // and the line after it is read as it should be
        Assert.Equal("physics.weather.arcs", Assert.Single(keys).Key);
    }

    [Fact]
    public void A_key_stands_once_the_first_entry_of_it_holding()
    {
        var keys = WeatherSchema.Parse(Header +
                                       Toggle("physics.weather.arcs", "1") +
                                       Toggle("physics.weather.ARCS", "0") +
                                       Toggle("physics.weather.arcs", "0"));

        // ARCS is not a key at all, the second arcs is the same as the first
        Assert.Equal("yes", Assert.Single(keys).Default);
    }

    [Fact]
    public void Numbers_are_read_with_a_point_whatever_the_language_of_the_machine()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NumberGroupSeparator = ".";
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            var key = Assert.Single(WeatherSchema.Parse(Header + Number("physics.weather.wet", "0.75", "0.3", "1")));

            Assert.Equal("0.75", key.Default);
            Assert.Equal(0.3, key.Minimum);
            Assert.Equal("0.55", key.FormatNumber(0.55));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void A_file_with_windows_line_ends_reads_the_same()
    {
        var text = Header + Toggle("physics.weather", "1") + Number("physics.weather.wet");

        var keys = WeatherSchema.Parse(text.Replace("\n", "\r\n"));

        Assert.Equal(new[] { "physics.weather", "physics.weather.wet" }, keys.Select(key => key.Key).ToArray());
        Assert.Equal("A number", keys[1].FallbackLabel);
    }

    [Fact]
    public void Fields_a_later_version_adds_to_the_end_of_a_line_are_not_in_the_way()
    {
        var keys = WeatherSchema.Parse(Header +
                                       "bool\tphysics.weather\t1\tmain\tA switch\tmore\tstill more\n" +
                                       "float\tphysics.weather.wet\t0.75\t0.3\t1\t2\tadhesion\tA number\textra\n");

        Assert.Equal(2, keys.Count);
        Assert.Equal("A switch", keys[0].FallbackLabel);
        Assert.Equal("A number", keys[1].FallbackLabel);
    }

    [Fact]
    public void A_label_left_empty_is_the_key_and_a_long_one_is_cut()
    {
        var keys = WeatherSchema.Parse(Header +
                                       Toggle("physics.weather.arcs", label: "   ") +
                                       Toggle("physics.weather.glaze", label: new string('x', 500)));

        Assert.Equal("physics.weather.arcs", keys[0].FallbackLabel);
        Assert.Equal(120, keys[1].FallbackLabel!.Length);
    }

    [Fact]
    public void A_file_or_a_line_too_long_and_too_many_entries_are_not_taken()
    {
        // a line past a kilobyte is left out, those around it are not
        var longLine = Toggle("physics.weather.glaze", label: new string('x', 1100));
        var keys = WeatherSchema.Parse(Header + Toggle("physics.weather.arcs") + longLine + Toggle("physics.weather.rimehard"));
        Assert.Equal(new[] { "physics.weather.arcs", "physics.weather.rimehard" }, keys.Select(key => key.Key).ToArray());

        // 600 entries are cut at 512
        var many = Header + string.Concat(Enumerable.Range(0, 600).Select(i => Toggle($"physics.weather.k{i}")));
        Assert.Equal(512, WeatherSchema.Parse(many).Count);

        // and a text of a quarter of a megabyte and more is not a file of this kind
        var huge = Header + new string('#', 256 * 1024);
        Assert.Empty(WeatherSchema.Parse(huge));
    }

    [Fact]
    public void The_file_is_read_from_the_folder_of_the_simulator()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(Path.Combine(TestInstallation.Root, WeatherSchema.FileName),
                      Header + Toggle("physics.weather.arcs", "1", "wires")));

        var keys = WeatherSchemaFile.Read(installation.Files, installation.Paths.FromRoot(WeatherSchema.FileName), installation.Log);

        Assert.Equal("physics.weather.arcs", Assert.Single(keys).Key);
    }

    [Fact]
    public void A_simulator_without_the_file_gives_no_keys_and_nothing_to_say_about_it()
    {
        var installation = new TestInstallation();

        var keys = WeatherSchemaFile.Read(installation.Files, installation.Paths.FromRoot(WeatherSchema.FileName), installation.Log);

        Assert.Empty(keys);
        Assert.DoesNotContain(WeatherSchema.FileName, installation.Log.Text);
    }

    [Fact]
    public void A_file_with_nothing_in_it_for_this_starter_is_said_so_in_the_log()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(Path.Combine(TestInstallation.Root, WeatherSchema.FileName), "schema\t2\nbool\tphysics.weather\t1\tmain\tA\n"));

        var keys = WeatherSchemaFile.Read(installation.Files, installation.Paths.FromRoot(WeatherSchema.FileName), installation.Log);

        Assert.Empty(keys);
        Assert.Contains("no weather settings", installation.Log.Text);
    }
}

/// <summary>
/// The keys of a simulator's description among the others: they are read from eu07.ini, shown, and written
/// only once moved. They are set for the whole program, so these tests take turns and leave none behind.
/// </summary>
[Collection("SimulatorKeys")]
public sealed class DescribedKeysInSettingsTests : IDisposable
{
    private static readonly string UserIni = Path.Combine("/home/kolejarz", ".config", "MaSzyna", "eu07.ini");

    public void Dispose() => SimulatorKeys.Register(Array.Empty<SimulatorKey>());

    private static TestInstallation WithIni(string ini)
    {
        var installation = new TestInstallation(new InMemoryFileSystem().WithFile(UserIni, ini));
        installation.Environment.With("HOME", "/home/kolejarz");
        return installation;
    }

    private static string[] SavedLines(TestInstallation installation) =>
        installation.Files.ReadAllText(UserIni).Replace("\r\n", "\n").Split('\n');

    private static void Describe() =>
        SimulatorKeys.Register(WeatherSchema.Parse("schema\t1\n" +
                                                   "bool\tphysics.weather.arcs\t1\twires\tArcs\n" +
                                                   "float\tphysics.weather.wet\t0.75\t0.3\t1\t2\tadhesion\tWet\n"));

    [Fact]
    public void They_come_after_the_built_in_keys_and_the_ones_set_before_go()
    {
        var builtIn = SimulatorKeys.All.Count;

        Describe();
        Assert.Equal(builtIn + 2, SimulatorKeys.All.Count);
        Assert.Equal("physics.weather.arcs", SimulatorKeys.All[builtIn].Key);
        Assert.Equal("Wet", SimulatorKeys.Named("physics.weather.wet").FallbackLabel);

        SimulatorKeys.Register(Array.Empty<SimulatorKey>());
        Assert.Equal(builtIn, SimulatorKeys.All.Count);
        Assert.DoesNotContain(SimulatorKeys.All, key => key.Key == "physics.weather.wet");
    }

    [Fact]
    public void A_key_the_starter_has_by_name_is_not_replaced_and_a_key_is_kept_once()
    {
        var own = SimulatorKeys.Named("ui.scale");
        var impostor = new SimulatorKey("ui.scale", SimulatorKeyKind.Toggle, "no", "general", "x");
        var twice = new SimulatorKey("physics.weather.arcs", SimulatorKeyKind.Toggle, "yes", "physics", "x");

        SimulatorKeys.Register(new[] { impostor, twice, twice });

        Assert.Same(own, SimulatorKeys.Named("ui.scale"));
        Assert.Single(SimulatorKeys.All, key => key.Key == "physics.weather.arcs");
    }

    [Fact]
    public void A_key_in_eu07_ini_is_read_into_the_settings_and_stays_as_it_was()
    {
        Describe();
        var installation = WithIni("width 1920\nphysics.weather.wet 0.5\nphysics.weather.arcs no\n");

        installation.SettingsStore.Load();
        var settings = installation.SettingsStore.Settings;

        Assert.Equal("0.5", settings.Extra["physics.weather.wet"]);
        Assert.Equal("no", settings.Extra["physics.weather.arcs"]);

        installation.SettingsStore.Save();
        var lines = SavedLines(installation);
        Assert.Contains("physics.weather.wet 0.5", lines);
        Assert.Contains("physics.weather.arcs no", lines);
    }

    [Fact]
    public void A_key_nobody_moved_is_not_written_so_the_simulator_goes_by_its_own_default()
    {
        Describe();
        var installation = WithIni("width 1920\n");

        installation.SettingsStore.Load();
        installation.SettingsStore.Save();

        Assert.DoesNotContain(SavedLines(installation), line => line.StartsWith("physics.weather", StringComparison.Ordinal));
    }

    [Fact]
    public void A_key_that_was_moved_is_written_with_a_point_whatever_the_language_of_the_machine()
    {
        Describe();
        var installation = WithIni("width 1920\n");
        installation.SettingsStore.Load();
        installation.SettingsStore.Settings.Extra["physics.weather.wet"] =
            SimulatorKeys.Named("physics.weather.wet").FormatNumber(0.55);
        installation.SettingsStore.Settings.Extra["physics.weather.arcs"] = SimulatorKey.FormatToggle(false);

        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            installation.SettingsStore.Save();
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }

        var lines = SavedLines(installation);
        Assert.Contains("physics.weather.wet 0.55", lines);
        Assert.Contains("physics.weather.arcs no", lines);
    }
}
