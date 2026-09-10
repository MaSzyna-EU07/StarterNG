using StarterNG.Domain.Sceneries;
using StarterNG.Infrastructure.Sceneries;
using StarterNG.Presentation.Scenarios;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class FogScaleTests
{
    [Fact]
    public void The_slider_ends_land_on_the_supported_visibility_range()
    {
        Assert.Equal(SceneryWeather.FogMin, FogScale.ToMetres(0));
        Assert.Equal(SceneryWeather.FogMax, FogScale.ToMetres(FogScale.SliderRange));
    }

    [Fact]
    public void A_distance_survives_a_round_trip_through_the_slider()
    {
        foreach (int metres in new[] { 50, 200, 1500, 8000 })
            Assert.Equal(metres, FogScale.ToMetres(FogScale.ToSlider(metres)));
    }

    [Fact]
    public void Distances_snap_to_a_step_that_grows_with_the_range()
    {
        Assert.Equal(0, FogScale.ToMetres(200) % 5);
        Assert.Equal(0, FogScale.ToMetres(700) % 100);
    }

    [Fact]
    public void Metres_below_a_kilometre_kilometres_above_it()
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        Assert.Equal("250 m", FogScale.Format(250, culture));
        Assert.Equal("2.5 km", FogScale.Format(2500, culture));
    }
}

public class SeasonDatesTests
{
    private static SeasonDates On(DateTime now) => new(new FixedClock(now));

    [Fact]
    public void A_stored_day_of_year_becomes_that_day_in_the_current_year()
    {
        var date = On(new DateTime(2026, 7, 1)).DateOf(100);

        Assert.Equal(2026, date.Year);
        Assert.Equal(100, date.DayOfYear);
    }

    [Fact]
    public void Day_zero_means_today()
    {
        var now = new DateTime(2026, 5, 4);

        Assert.Equal(now.DayOfYear, On(now).DateOf(0).DayOfYear);
    }

    [Fact]
    public void The_last_day_of_a_leap_year_stays_selectable()
    {
        Assert.Equal(366, On(new DateTime(2028, 1, 1)).DateOf(366).DayOfYear);
        Assert.Equal(365, On(new DateTime(2026, 1, 1)).DateOf(400).DayOfYear);
    }
}

public class SceneryTreeBuilderTests
{
    private static (SceneryTreeBuilder Builder, List<Scenery> Sceneries) Build(params string[] contents)
    {
        var files = new InMemoryFileSystem();
        for (int i = 0; i < contents.Length; i++)
            files.WithLegacyFile(TestInstallation.At("scenery", $"s{i}.scn"), contents[i]);

        var installation = new TestInstallation(files);
        var sceneries = installation.Sceneries.LoadAll().ToList();
        return (new SceneryTreeBuilder(new SceneryTranslations(files, installation.Log)), sceneries);
    }

    [Fact]
    public void Scenarios_declaring_a_group_are_gathered_under_it()
    {
        var (builder, sceneries) = Build("//$l Śląsk", "//$l Śląsk", "//$n Bez grupy");

        var nodes = builder.Build(sceneries, includeArchival: true, "en");

        var group = Assert.Single(nodes, node => node.IsGroup);
        Assert.Equal("Śląsk", group.Label);
        Assert.Equal(2, group.Children.Count);
        Assert.Single(nodes, node => !node.IsGroup);
    }

    [Fact]
    public void Archival_scenarios_are_left_out_unless_asked_for()
    {
        var (builder, sceneries) = Build("//$a", "//$n Zwykla");

        Assert.Single(builder.Build(sceneries, includeArchival: false, "en"));
        Assert.Equal(2, builder.Build(sceneries, includeArchival: true, "en").Count);
    }

    [Fact]
    public void Nodes_carry_the_index_of_the_scenery_they_stand_for()
    {
        var (builder, sceneries) = Build("//$n Pierwsza", "//$n Druga");

        var nodes = builder.Build(sceneries, includeArchival: true, "en");

        foreach (var node in nodes)
            Assert.Equal(node.Label, sceneries[node.SceneryIndex].DisplayName);
    }

    [Fact]
    public void Both_levels_are_sorted_by_name()
    {
        var (builder, sceneries) = Build("//$l Zet", "//$l Alfa", "//$l Alfa");

        var nodes = builder.Build(sceneries, includeArchival: true, "en");

        Assert.Equal(new[] { "Alfa", "Zet" }, nodes.Select(node => node.Label));
        Assert.Equal(nodes[0].Children.Select(child => child.Label).OrderBy(label => label),
                     nodes[0].Children.Select(child => child.Label));
    }
}

public class TimetableLocatorTests
{
    [Fact]
    public void A_timetable_is_found_in_the_shared_folder_first()
    {
        var files = new InMemoryFileSystem()
            .WithFile(TestInstallation.At("scenery", "kato.scn"), "//$n Katowice")
            .WithFile(TestInstallation.At("timetables", "r1.txt"), "rozkład")
            .WithFile(TestInstallation.At("scenery", "r1.txt"), "rozkład obok scenerii");
        var scenery = Assert.Single(new TestInstallation(files).Sceneries.LoadAll());

        Assert.Equal(TestInstallation.At("timetables", "r1.txt"),
                     new TimetableLocator(files).Resolve(scenery, "r1"));
    }

    [Fact]
    public void A_name_already_carrying_its_extension_is_found_too()
    {
        var files = new InMemoryFileSystem()
            .WithFile(TestInstallation.At("scenery", "kato.scn"), "//$n Katowice")
            .WithFile(TestInstallation.At("timetables", "r1.txt"), "rozkład");
        var scenery = Assert.Single(new TestInstallation(files).Sceneries.LoadAll());

        Assert.NotNull(new TimetableLocator(files).Resolve(scenery, "r1.txt"));
    }

    [Fact]
    public void The_formats_none_and_a_missing_file_both_resolve_to_nothing()
    {
        var files = new InMemoryFileSystem()
            .WithFile(TestInstallation.At("scenery", "kato.scn"), "//$n Katowice");
        var scenery = Assert.Single(new TestInstallation(files).Sceneries.LoadAll());
        var locator = new TimetableLocator(files);

        Assert.Null(locator.Resolve(scenery, "none"));
        Assert.Null(locator.Resolve(scenery, "nieistniejacy"));
        Assert.Null(locator.Resolve(scenery, null));
    }
}
