using StarterNG.Domain.Sceneries;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class SceneryWeatherTests
{
    [Fact]
    public void Authored_weather_is_read_from_the_config_block()
    {
        const string scn = """
            config
            movelight 172
            scenario.weather.temperature 21.5
            scenario.time.override 6:05
            endconfig
            atmo 0 0 0 50 900 0 0 0 0.7 endatmo
            time 8:30 0 0 endtime
            """;

        var weather = TestScenery.Parser(new DateTime(2026, 1, 1)).Parse("scenery/x.scn", scn).Weather;

        Assert.True(weather.IsAuthored);
        Assert.Equal(172, weather.Day);
        Assert.Equal(21.5, weather.Temperature);
        Assert.Equal("06:05", weather.ScenarioTimeOverride);
        Assert.Equal("08:30", weather.Time);
        Assert.Equal(50, weather.FogStart);
        Assert.Equal(0.7, weather.Overcast);
    }

    [Fact]
    public void A_scenery_without_a_time_starts_at_the_players_clock()
    {
        var weather = TestScenery.Parser(new DateTime(2026, 5, 4, 17, 42, 0)).Parse("scenery/x.scn", "//$n Test").Weather;

        Assert.Equal("17:42", weather.ScenarioTimeOverride);
        Assert.False(weather.IsAuthored);
    }

    [Fact]
    public void Freshly_parsed_weather_counts_as_unchanged()
    {
        var weather = TestScenery.Parser(new DateTime(2026, 1, 1)).Parse("scenery/x.scn", "movelight 100\n").Weather;

        Assert.False(weather.Changed);
    }

    [Fact]
    public void Editing_then_restoring_returns_to_the_authored_values()
    {
        var weather = TestScenery.Parser(new DateTime(2026, 1, 1)).Parse("scenery/x.scn", "movelight 100\n").Weather;

        weather.Day = 5;
        weather.Temperature = -12;
        Assert.True(weather.Changed);

        weather.Restore();

        Assert.False(weather.Changed);
        Assert.Equal(100, weather.Day);
        Assert.True(weather.Dirty);
    }

    [Fact]
    public void A_fog_range_is_clamped_into_the_supported_span()
    {
        var weather = new SceneryWeather();

        weather.SetFogRange(start: 1, end: 999999, random: new StubRandom());

        Assert.Equal(SceneryWeather.FogMin, weather.FogStart);
        Assert.InRange(weather.FogEnd, SceneryWeather.FogMin, SceneryWeather.FogMax);
    }

    [Fact]
    public void Random_overcast_draws_from_the_presets_rather_than_the_stored_value()
    {
        var weather = new SceneryWeather { Overcast = 0.42, OvercastRandom = true };

        Assert.Equal(-1.5, weather.EffectiveOvercast(new StubRandom()));
        Assert.Equal(0.42, new SceneryWeather { Overcast = 0.42 }.EffectiveOvercast(new StubRandom()));
    }

    private static string Rewritten(string scn) =>
        SceneryWeatherWriter.Rewrite(scn, new SceneryWeather(), new StubRandom());

    private static string Code(string text) => SceneryCode.Mask(text, out _);

    /// <summary>The block the writer puts at the top of the text, and the rest.</summary>
    private static (string Written, string Others) Split(string rewritten)
    {
        const string end = "endtime\r\n";
        int at = rewritten.IndexOf(end, StringComparison.Ordinal) + end.Length;
        return (rewritten[..at], rewritten[at..]);
    }

    [Fact]
    public void A_config_commented_out_stays_off_and_the_one_after_it_is_kept()
    {
        // As linia61_osobowy2.scn has them.
        const string commented = "//config pause yes endconfig //yes: zatrzymanie symulacji zaraz po wczytaniu, jeśli start jako jadący";
        string scn = $"""
            sky skydome_clouds.t3d endsky
            atmo 0 0 0 1000 2000 0.341 0.408 0.471 -1.2 endatmo
            time 6:43 06:07 21:02 endtime //w Lublińcu 7:30

            {commented}
            config hiddenevents 1 endconfig

            include linia61/czestochowa.scm end

            """;

        var (written, rest) = Split(Rewritten(scn));

        Assert.Contains("hiddenevents 1\r\n", written);
        Assert.DoesNotContain("pause", written);
        Assert.Contains(commented, rest);
        Assert.Contains("//w Lublińcu 7:30", rest);
        Assert.Contains("sky skydome_clouds.t3d endsky", rest);
        Assert.Contains("include linia61/czestochowa.scm end", rest);
        Assert.DoesNotContain("hiddenevents", rest);
        foreach (string word in new[] { "atmo", "time", "pause" })
            Assert.DoesNotContain(word, Code(rest));
    }

    [Fact]
    public void A_keyword_in_a_comment_does_not_cut_up_to_the_next_block()
    {
        const string scn = """
            // atmo: tu byla mgla
            include a.scm end
            {{0}}
            /* config */
            include b.scm end
            {{1}}
            // time 8 tu nie
            include c.scm end
            atmo 0 0 0 10 2000 0 0 0 0 endatmo
            config hiddenevents 1 endconfig
            time 8:30 0 0 endtime
            """;

        var (written, rest) = Split(Rewritten(scn));

        Assert.Contains("hiddenevents 1", written);
        foreach (string kept in new[]
                 {
                     "// atmo: tu byla mgla", "include a.scm end", "{{0}}", "/* config */",
                     "include b.scm end", "{{1}}", "// time 8 tu nie", "include c.scm end"
                 })
            Assert.Contains(kept, rest);
        foreach (string gone in new[] { "endatmo", "endconfig", "endtime" })
            Assert.DoesNotContain(gone, rest);
    }

    [Theory]
    [InlineData("/* config a 1 endconfig */")]
    [InlineData("// config a 1 endconfig")]
    [InlineData("/* atmo 0 0 0 1 2 0 0 0 0 endatmo */")]
    [InlineData("// atmo 0 0 0 1 2 0 0 0 0 endatmo")]
    [InlineData("/* time 8:30 0 0 endtime */")]
    [InlineData("// time 8:30 0 0 endtime")]
    [InlineData("// movelight 100")]
    [InlineData("/*\nmovelight 100\n*/")]
    [InlineData("// scenario.weather.temperature 5")]
    [InlineData("/* scenario.time.override 6:00 */")]
    public void What_is_commented_out_stays_as_it_is(string comment)
    {
        var (written, rest) = Split(Rewritten($"x\n{comment}\ny\n"));

        Assert.Equal($"x\n{comment}\ny\n", rest);
        Assert.DoesNotContain("a 1", written);
    }

    [Fact]
    public void A_comment_in_a_block_stays_when_the_block_goes()
    {
        const string scn = "atmo 0 0 0 // mgla\n10 2000 0 0 0 /* zachmurzenie */ 0 endatmo\n" +
                           "config\n// uwaga\nmovelight 5 // lato\nhiddenevents 1\nendconfig\ninclude a.scm end\n";

        var (written, rest) = Split(Rewritten(scn));

        foreach (string comment in new[] { "// mgla\n", "/* zachmurzenie */", "// uwaga\n", "// lato\n" })
            Assert.Contains(comment, rest);
        foreach (string word in new[] { "atmo", "2000", "config", "movelight", "hiddenevents" })
            Assert.DoesNotContain(word, Code(rest));
        Assert.Contains("hiddenevents 1\r\n", written);
        Assert.Contains("include a.scm end", rest);
    }

    [Fact]
    public void Settings_of_every_config_are_carried_and_only_the_starters_own_are_replaced()
    {
        const string scn = "config movelight 5 hiddenevents 1 endconfig\nCONFIG\nscenario.weather.temperature 3 pause no\nENDCONFIG\n";

        var (written, rest) = Split(Rewritten(scn));

        Assert.Contains("hiddenevents 1\r\n", written);
        Assert.Contains("pause no\r\n", written);
        Assert.DoesNotContain("movelight 5", written);
        Assert.DoesNotContain("temperature 3", written);
        Assert.Equal("", rest.Trim());
    }

    [Fact]
    public void A_word_in_an_include_s_path_is_no_keyword()
    {
        const string scn = """
            include atmo/lasy.scm end
            include time/8.scm end
            include config/a.scm end
            atmo 0 0 0 10 2000 0 0 0 0 endatmo
            config hiddenevents 1 endconfig
            """;

        var (_, rest) = Split(Rewritten(scn));

        Assert.Contains("include atmo/lasy.scm end", rest);
        Assert.Contains("include time/8.scm end", rest);
        Assert.Contains("include config/a.scm end", rest);
        Assert.DoesNotContain("endatmo", rest);
    }

    [Fact]
    public void Weather_in_comments_is_not_read()
    {
        const string scn = """
            // movelight 7
            // scenario.weather.temperature 40
            // scenario.time.override 3:33
            /* time 4:44 0 0 endtime */
            // atmo 5 mgla w l61
            include l61/a.scm end
            movelight 100
            atmo 0 0 0 50 900 0 0 0 0.7 endatmo
            """;

        var weather = TestScenery.Parser(new DateTime(2026, 5, 4, 17, 42, 0)).Parse("scenery/x.scn", scn).Weather;

        Assert.Equal(100, weather.Day);
        Assert.NotEqual(40, weather.Temperature);
        Assert.Equal("17:42", weather.ScenarioTimeOverride);
        Assert.Equal(50, weather.FogStart);
        Assert.Equal(0.7, weather.Overcast);
    }
}
