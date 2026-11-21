using StarterNG.Classes;
using StarterNG.Domain;
using StarterNG.Domain.Settings;
using StarterNG.Views;

namespace StarterNG.Tests;

public class TrainsetDisplayTests
{
    private static Trainset Consist(string driver = "headdriver", string? description = null) =>
        new(string.Concat(
            description is null ? "" : $"//$o {description}\n",
            $"trainset a tor 0 0\nnode -1 0 v1 dynamic d s m 0 {driver} 3 0 enddynamic\nendtrainset"));

    [Theory]
    [InlineData("-ai", true)]
    [InlineData("  - ai", true)]
    [InlineData("a - b", false)]
    [InlineData("rozkladowy", false)]
    public void A_dash_opening_the_description_says_the_consist_is_ai(string description, bool ai) =>
        Assert.Equal(ai, TrainsetDisplay.IsAi(Consist(description: description)));

    [Fact]
    public void No_description_is_no_ai() =>
        Assert.False(TrainsetDisplay.IsAi(Consist()));

    [Fact]
    public void An_ai_consist_is_listed_only_when_they_are_shown()
    {
        var ai = Consist(description: "-ai");

        Assert.False(TrainsetDisplay.ListedIn(ai, showAi: false, drivableOnly: false, current: null));
        Assert.True(TrainsetDisplay.ListedIn(ai, showAi: true, drivableOnly: false, current: null));
    }

    [Fact]
    public void Drivable_only_leaves_out_a_consist_nobody_sits_in()
    {
        var empty = Consist(driver: "nobody");

        Assert.False(TrainsetDisplay.ListedIn(empty, showAi: true, drivableOnly: true, current: null));
        Assert.True(TrainsetDisplay.ListedIn(empty, showAi: true, drivableOnly: false, current: null));
        Assert.True(TrainsetDisplay.ListedIn(Consist(), showAi: true, drivableOnly: true, current: null));
    }

    [Fact]
    public void Drivable_only_keeps_the_one_picked_whatever_its_crew()
    {
        var picked = Consist(driver: "nobody");

        Assert.True(TrainsetDisplay.ListedIn(picked, showAi: true, drivableOnly: true, current: picked));
    }

    [Fact]
    public void Being_picked_does_not_show_an_ai_consist_the_filter_hides()
    {
        var picked = Consist(description: "-ai");

        Assert.False(TrainsetDisplay.ListedIn(picked, showAi: false, drivableOnly: true, current: picked));
    }

    private static SimulatorSettings Mode(bool advanced) =>
        new() { AdvancedSettings = advanced, ShowAiVehicles = true, DrivableOnly = false };

    [Fact]
    public void The_standard_mode_lists_every_consist_but_starts_only_in_one_a_player_drives()
    {
        var standard = Mode(advanced: false);
        var decor = new Trainset("trainset a tor 0 0 //$decor\nnode -1 0 v1 dynamic d s m 0 headdriver 3 0 enddynamic\nendtrainset");
        Assert.True(decor.Decor);

        foreach (var consist in new[] { Consist(), Consist(description: "-ai"), Consist(driver: "nobody"), decor })
            Assert.True(TrainsetDisplay.ListedIn(consist, standard, current: null));

        Assert.True(TrainsetDisplay.StartableIn(Consist(), standard));
        Assert.True(TrainsetDisplay.StartableIn(Consist(driver: "nobody"), standard));
        Assert.False(TrainsetDisplay.StartableIn(Consist(description: "-ai"), standard));
        Assert.False(TrainsetDisplay.StartableIn(decor, standard));
        Assert.True(TrainsetDisplay.StartableIn(decor, Mode(advanced: true)));
    }

    [Fact]
    public void The_filters_work_in_the_advanced_mode()
    {
        var settings = Mode(advanced: true);

        settings.ShowAiVehicles = false;
        Assert.False(TrainsetDisplay.ListedIn(Consist(description: "-ai"), settings, current: null));
        Assert.True(TrainsetDisplay.ListedIn(Consist(), settings, current: null));

        settings.DrivableOnly = true;
        Assert.False(TrainsetDisplay.ListedIn(Consist(driver: "nobody"), settings, current: null));
    }

    [Fact]
    public void The_standard_mode_has_no_filters_and_leaves_out_empty_consists()
    {
        var standard = new SimulatorSettings { AdvancedSettings = false, ShowAiVehicles = false, DrivableOnly = true };
        var empty = new Trainset("trainset a tor 0 0\nendtrainset");

        Assert.True(TrainsetDisplay.ListedIn(Consist(description: "-ai"), standard, current: null));
        Assert.True(TrainsetDisplay.ListedIn(Consist(driver: "nobody"), standard, current: null));
        Assert.False(TrainsetDisplay.ListedIn(empty, standard, current: null));
        Assert.False(TrainsetDisplay.ListedIn(empty, standard, current: empty));
    }

    [Fact]
    public void The_advanced_mode_lists_by_its_own_filters()
    {
        var advanced = Mode(advanced: true);

        Assert.True(TrainsetDisplay.ListedIn(Consist(description: "-ai"), advanced, current: null));
        Assert.True(TrainsetDisplay.ListedIn(Consist(driver: "nobody"), advanced, current: null));

        advanced.ShowAiVehicles = false;
        Assert.False(TrainsetDisplay.ListedIn(Consist(description: "-ai"), advanced, current: null));
    }

    [Theory]
    [InlineData(eDriverType.Headdriver, 0)]
    [InlineData(eDriverType.Reardriver, 1)]
    [InlineData(eDriverType.Passenger, 2)]
    [InlineData(eDriverType.Nobody, 3)]
    public void A_crew_keeps_its_place_in_the_list(eDriverType crew, int place)
    {
        Assert.Equal(place, CrewChoices.IndexOf(crew));
        Assert.Equal(crew, CrewChoices.At(place));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void A_list_with_nothing_selected_means_nobody(int place) =>
        Assert.Equal(eDriverType.Nobody, CrewChoices.At(place));
}
