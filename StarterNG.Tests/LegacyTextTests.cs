using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Tests;

public class LegacyTextTests
{
    [Fact]
    public void The_game_code_page_resolves_without_anyone_registering_it_first()
    {
        // Regression guard: when this falls back, every Polish character in a
        // scenery description comes out as Latin-1 mojibake ("Skład" as "Sk³ad").
        Assert.False(LegacyText.IsFallback);
        Assert.Equal(1250, LegacyText.CodePage1250.CodePage);
    }

    [Fact]
    public void Polish_characters_survive_a_round_trip_through_it()
    {
        const string text = "Skład wygaszony pod wjazdowym. Strzebiń - Woźniki Śl.";

        byte[] bytes = LegacyText.CodePage1250.GetBytes(text);

        Assert.Equal(text, LegacyText.CodePage1250.GetString(bytes));
    }

    [Fact]
    public void A_scenery_written_in_the_game_code_page_reads_back_intact()
    {
        var files = new StarterNG.Tests.Fakes.InMemoryFileSystem()
            .WithLegacyFile(StarterNG.Tests.Fakes.TestInstallation.At("scenery", "td.scn"),
                            "//$n Strzebiń - Woźniki Śl. '92\n//$d Skład wygaszony pod wjazdowym.");

        var scenery = Assert.Single(new StarterNG.Tests.Fakes.TestInstallation(files).Sceneries.LoadAll());

        Assert.Equal("Strzebiń - Woźniki Śl. '92", scenery.Name);
        Assert.Equal("Skład wygaszony pod wjazdowym.", scenery.Description);
    }
}
