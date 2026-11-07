using StarterNG.Domain.Settings;

namespace StarterNG.Tests;

public class ResolutionChoicesTests
{
    private static List<string> Listed(ScreenSize[] monitors, ScreenSize current) =>
        ResolutionChoices.For(monitors, current).Select(c => c.Size.ToString()).ToList();

    [Fact]
    public void The_monitors_come_first_and_are_marked()
    {
        var choices = ResolutionChoices.For(new[] { new ScreenSize(1920, 1200), new ScreenSize(2560, 1440) },
                                             new ScreenSize(1280, 720));

        Assert.Equal(new ScreenSize(1920, 1200), choices[0].Size);
        Assert.Equal(new ScreenSize(2560, 1440), choices[1].Size);
        Assert.True(choices[0].IsMonitor);
        Assert.True(choices[1].IsMonitor);
        Assert.All(choices.Skip(2), c => Assert.False(c.IsMonitor));
    }

    [Fact]
    public void A_size_no_monitor_can_show_is_not_offered()
    {
        var listed = Listed(new[] { new ScreenSize(1920, 1080) }, new ScreenSize(1280, 720));

        Assert.Contains("1920x1080", listed);
        Assert.Contains("1600x900", listed);
        Assert.DoesNotContain("3840x2160", listed);
        Assert.DoesNotContain("2560x1440", listed);
        // Taller than the screen, though no wider.
        Assert.DoesNotContain("1920x1200", listed);
    }

    [Fact]
    public void A_size_fitting_any_of_the_monitors_is_offered()
    {
        var listed = Listed(new[] { new ScreenSize(1920, 1080), new ScreenSize(3440, 1440) }, new ScreenSize(1280, 720));

        Assert.Contains("2560x1080", listed);
        Assert.DoesNotContain("3840x2160", listed);
    }

    [Theory]
    [InlineData(1920, 1200)]
    [InlineData(1280, 800)]
    [InlineData(2560, 1080)]
    [InlineData(3440, 1440)]
    public void The_monitors_size_is_there_even_where_it_is_not_a_usual_one(int width, int height)
    {
        var listed = Listed(new[] { new ScreenSize(width, height) }, new ScreenSize(1024, 768));

        Assert.Equal($"{width}x{height}", listed[0]);
    }

    [Fact]
    public void The_size_asked_for_stays_on_the_list_whatever_it_is()
    {
        var listed = Listed(new[] { new ScreenSize(1920, 1080) }, new ScreenSize(5120, 1440));

        Assert.Contains("5120x1440", listed);
        Assert.Single(listed, "5120x1440");
    }

    [Fact]
    public void The_rest_goes_from_the_largest_down_without_repeats()
    {
        var listed = Listed(new[] { new ScreenSize(2560, 1440) }, new ScreenSize(1920, 1080));

        Assert.Equal(listed.Count, listed.Distinct().Count());
        var sizes = ResolutionChoices.For(new[] { new ScreenSize(2560, 1440) }, new ScreenSize(1920, 1080))
            .Skip(1).Select(c => c.Size).ToList();
        Assert.Equal(sizes.OrderByDescending(s => s.Width).ThenByDescending(s => s.Height), sizes);
    }

    [Fact]
    public void Two_monitors_of_a_size_are_one_entry()
    {
        var choices = ResolutionChoices.For(new[] { new ScreenSize(1920, 1080), new ScreenSize(1920, 1080) },
                                             new ScreenSize(1920, 1080));

        Assert.Single(choices, c => c.Size == new ScreenSize(1920, 1080));
    }

    [Fact]
    public void With_no_monitors_known_everything_usual_is_offered()
    {
        var listed = Listed(Array.Empty<ScreenSize>(), new ScreenSize(1280, 720));

        Assert.Contains("3840x2160", listed);
        Assert.Contains("1280x800", listed);
    }

    [Fact]
    public void A_size_of_nothing_is_not_listed()
    {
        var listed = Listed(new[] { new ScreenSize(0, 0) }, new ScreenSize(0, 0));

        Assert.DoesNotContain("0x0", listed);
        Assert.NotEmpty(listed);
    }
}
