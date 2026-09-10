using StarterNG.Infrastructure.Sceneries;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class SceneryExportTests
{
    private static SceneryParser Parser() =>
        new(new FixedClock(new DateTime(2026, 1, 1, 12, 0, 0)), new StubRandom());

    [Fact]
    public void An_untouched_scenery_exports_its_trainsets_back_verbatim()
    {
        const string scn = """
            //$n Test
            trainset pociag tor_1 10 0
            node -1 -1 ep07-001 dynamic PKP/EP07 ep07 ep07.mmd 0 headdriver 0
            endtrainset
            FirstInit
            """;
        var scenery = Parser().Parse("scenery/x.scn", scn);

        string exported = scenery.BuildExportContent(skipDecorTrainsets: false, new StubRandom());

        Assert.DoesNotContain("{{0}}", exported);
        Assert.Contains("trainset pociag tor_1", exported);
        Assert.Contains("endtrainset", exported);
    }

    [Fact]
    public void Selected_optional_includes_are_written_back_before_FirstInit()
    {
        const string scn = """
            include zima.inc end //$optional 1, Zima
            x
            endoptional
            FirstInit
            """;
        var scenery = Parser().Parse("scenery/x.scn", scn);

        string exported = scenery.BuildExportContent(skipDecorTrainsets: false, new StubRandom());

        Assert.Contains("include zima.inc end", exported);
        Assert.True(exported.IndexOf("include zima.inc", StringComparison.Ordinal) <
                    exported.IndexOf("FirstInit", StringComparison.Ordinal));
    }

    [Fact]
    public void A_deselected_optional_include_stays_out()
    {
        const string scn = """
            include zima.inc end //$optional 1, Zima
            x
            endoptional
            FirstInit
            """;
        var scenery = Parser().Parse("scenery/x.scn", scn);
        scenery.Includes[0].Selected = false;

        string exported = scenery.BuildExportContent(skipDecorTrainsets: false, new StubRandom());

        Assert.DoesNotContain("include zima.inc", exported);
    }

    [Fact]
    public void A_kind_two_include_is_written_only_without_a_companion_timetable()
    {
        const string scn = """
            include rozklad.inc end //$optional 2, Rozkład
            x
            endoptional
            FirstInit
            """;
        var scenery = Parser().Parse("scenery/x.scn", scn);

        scenery.HasCompanionTimetable = false;
        Assert.Contains("include rozklad.inc",
                        scenery.BuildExportContent(skipDecorTrainsets: false, new StubRandom()));

        scenery.HasCompanionTimetable = true;
        Assert.DoesNotContain("include rozklad.inc",
                              scenery.BuildExportContent(skipDecorTrainsets: false, new StubRandom()));
    }

    [Fact]
    public void Edited_weather_replaces_the_authored_config_block()
    {
        const string scn = """
            config
            movelight 100
            scenario.weather.temperature 5
            eventlauncher.range 200
            endconfig
            atmo 0 0 0 10 2000 0 0 0 0 endatmo
            time 8:30 0 0 endtime
            FirstInit
            """;
        var scenery = Parser().Parse("scenery/x.scn", scn);

        scenery.Weather.Day = 200;
        scenery.Weather.Temperature = 25;
        scenery.Weather.Dirty = true;

        string exported = scenery.BuildExportContent(skipDecorTrainsets: false, new StubRandom());

        Assert.Contains("movelight 200", exported);
        Assert.Contains("scenario.weather.temperature 25", exported);
        Assert.DoesNotContain("movelight 100", exported);
        // Config the scenery author wrote and the starter does not manage survives.
        Assert.Contains("eventlauncher.range 200", exported);
    }

    [Fact]
    public void Decor_trainsets_can_be_dropped_from_the_export()
    {
        const string scn = """
            //$n Test
            trainset ai_1 tor_1 10 0 //$decor
            node -1 -1 ai-1 dynamic PKP/EP07 ep07 ep07.mmd 0 nobody 0
            endtrainset
            FirstInit
            """;
        var scenery = Parser().Parse("scenery/x.scn", scn);

        Assert.True(scenery.Trainsets[0].Decor);
        Assert.DoesNotContain("trainset ai_1",
                              scenery.BuildExportContent(skipDecorTrainsets: true, new StubRandom()));
        Assert.Contains("trainset ai_1",
                        scenery.BuildExportContent(skipDecorTrainsets: false, new StubRandom()));
    }

    [Fact]
    public void Loose_vehicles_are_appended_as_nodes_again()
    {
        const string scn = """
            node -1 -1 wagon_1 dynamic PKP/111A 111a 111a.mmd none 0 nobody 0 enddynamic
            FirstInit
            """;
        var scenery = Parser().Parse("scenery/x.scn", scn);

        string exported = scenery.BuildExportContent(skipDecorTrainsets: false, new StubRandom());

        Assert.Contains("wagon_1", exported);
        Assert.Contains("enddynamic", exported);
    }
}
