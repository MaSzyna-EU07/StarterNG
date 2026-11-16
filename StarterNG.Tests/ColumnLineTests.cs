using Avalonia;
using StarterNG.Views.Tapes;

namespace StarterNG.Tests;

/// <summary>The line of a tape, drawn a column of pixels at a time and broken off at a gap.</summary>
public class ColumnLineTests
{
    private readonly List<string> _drawn = [];

    private ColumnLine Line() =>
        new(p => _drawn.Add($"begin {p.X} {p.Y}"), p => _drawn.Add($"line {p.X} {p.Y}"), () => _drawn.Add("end"));

    [Fact]
    public void A_column_is_entered_where_it_started_and_left_where_it_ended_through_its_lowest_and_highest()
    {
        var line = Line();

        // Falling within the column: from its first through its highest and its lowest to its last.
        foreach (double v in new[] { 3.0, 1.0, 4.0, 2.0 })
            line.Add(5, v);
        // Rising: through its lowest first.
        foreach (double v in new[] { 2.0, 4.0, 1.0, 3.0 })
            line.Add(6, v);
        line.Break();

        Assert.Equal(["begin 5 3", "line 5 4", "line 5 1", "line 5 2",
                      "line 6 2", "line 6 1", "line 6 4", "line 6 3", "end"], _drawn);
    }

    [Fact]
    public void A_break_ends_the_figure_and_the_next_point_begins_another()
    {
        var line = Line();

        line.Add(0, 10);
        line.Break();
        line.Add(9, 20);
        line.Break();

        Assert.Equal(["begin 0 10", "line 0 10", "line 0 10", "line 0 10", "end",
                      "begin 9 20", "line 9 20", "line 9 20", "line 9 20", "end"], _drawn);
    }

    [Fact]
    public void A_break_with_nothing_drawn_ends_nothing()
    {
        var line = Line();

        line.Break();
        line.Break();

        Assert.Empty(_drawn);
    }
}
