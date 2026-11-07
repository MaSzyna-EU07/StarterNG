using StarterNG.Classes;
using StarterNG.Domain;
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
