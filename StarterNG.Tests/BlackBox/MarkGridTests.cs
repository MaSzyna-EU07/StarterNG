using Avalonia;
using Eu07.BlackBox;
using Eu07.BlackBox.Views;

namespace StarterNG.Tests.BlackBox;

/// <summary>The marks of the black box map thinned out where they crowd, the most telling kept.</summary>
public class MarkGridTests
{
    [Fact]
    public void A_crowd_in_one_square_leaves_its_most_telling_mark()
    {
        var kept = MarkGrid.Thin(new[]
        {
            ("go", new Point(1, 1), 2),
            ("stop", new Point(3, 2), 0),
            ("speed", new Point(4, 4), 1)
        }, 6);

        Assert.Equal(["stop"], kept.Select(k => k.Item));
    }

    [Fact]
    public void Marks_apart_by_more_than_a_square_are_all_kept()
    {
        var marks = Enumerable.Range(0, 20).Select(i => (i, new Point(i * 10, 5), 1)).ToList();

        Assert.Equal(20, MarkGrid.Thin(marks, 6).Count);
    }

    [Fact]
    public void Of_marks_alike_the_first_keeps_its_square_and_the_telling_ones_are_drawn_last()
    {
        var kept = MarkGrid.Thin(new[]
        {
            ("first", new Point(1, 1), 1),
            ("second", new Point(2, 2), 1),
            ("apart", new Point(50, 50), 0)
        }, 6);

        Assert.Equal(["first", "apart"], kept.Select(k => k.Item));
    }

    [Fact]
    public void A_row_closer_than_a_square_keeps_one_to_each()
    {
        // A hundred marks a pixel apart are as many squares of six as they cover.
        var marks = Enumerable.Range(0, 100).Select(i => (i, new Point(i, 0), 1)).ToList();

        Assert.Equal(17, MarkGrid.Thin(marks, 6).Count);
    }

    [Theory]
    [InlineData(0f, 40f, 0)]    // at stop
    [InlineData(40f, -1f, 1)]   // a speed
    [InlineData(-1f, -1f, 2)]   // go
    public void A_signal_at_stop_tells_most(float speed1, float speed2, int rank)
    {
        Assert.Equal(rank, MapView.SignalRank(new AspectChange(0, 1, speed1, speed2, "")));
    }

    [Fact]
    public void A_signal_not_yet_seen_tells_least()
    {
        Assert.Equal(3, MapView.SignalRank(null));
    }
}
