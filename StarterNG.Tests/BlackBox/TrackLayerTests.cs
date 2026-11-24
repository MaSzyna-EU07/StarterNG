using System.Numerics;
using Avalonia;
using Eu07.BlackBox;
using Eu07.BlackBox.Views;

namespace StarterNG.Tests.BlackBox;

/// <summary>The rails of the black box map made ready to draw only what is in view, at the detail a pixel shows.</summary>
[Collection(AvaloniaControls.Name)]
public class TrackLayerTests
{
    public TrackLayerTests() => HeadlessDrawing.Ensure();

    private static MapTrack Track(int index, TrackKind kind, params (float X, float Z)[] points) =>
        new(index, $"t{index}", kind, kind == TrackKind.SwitchBranch ? 0 : -1, 0,
            points.Select(p => new Vector3(p.X, 0, p.Z)).ToList());

    [Theory]
    [InlineData(2.0, 0)]     // close up: every point
    [InlineData(0.3, 1)]     // 2 m are 0.6 px, 8 m would be 2.4
    [InlineData(0.05, 2)]    // 8 m are 0.4 px, 32 m would be 1.6
    [InlineData(0.002, 4)]   // the whole of a big scenery: 128 m are 0.26 px
    public void The_level_leaves_out_no_more_than_a_pixel_shows(double scale, int level)
    {
        Assert.Equal(level, TrackLayer.LevelFor(scale));
    }

    [Fact]
    public void Only_the_cells_in_view_are_drawn()
    {
        var layer = new TrackLayer(new[]
        {
            Track(1, TrackKind.Normal, (0, 0), (100, 0)),
            Track(2, TrackKind.Normal, (-5000, -5000), (-5100, -5000))
        });

        // The plane shows x to the left: the first track lies at -100..0, the second at 5000..5100.
        var near = layer.CellsIn(new Rect(-200, -50, 300, 100), 0).ToList();

        Assert.Single(near);
        Assert.Equal(2, layer.CellCount(0));
    }

    [Fact]
    public void A_track_across_cells_is_drawn_without_a_gap_at_the_border()
    {
        var layer = new TrackLayer(new[] { Track(1, TrackKind.Normal, (-400, 0), (-600, 0), (-800, 0)) });

        var cells = layer.CellsIn(new Rect(-10000, -10000, 20000, 20000), 0).ToList();

        // The segment over the border goes with the cell it ends in, from the point the line left the last one:
        // that cell reaches back to it, so it is drawn whenever any of the segment is in view.
        var cell = Assert.Single(cells);
        Assert.Equal(400, cell.Bounds.Left);
        Assert.Equal(800, cell.Bounds.Right);
        Assert.Single(layer.CellsIn(new Rect(450, -10, 20, 20), 0));
    }

    [Fact]
    public void A_switch_way_is_in_the_casing_but_not_in_the_plain_fill()
    {
        var layer = new TrackLayer(new[] { Track(7, TrackKind.SwitchBranch, (0, 0), (30, 5)) });

        var cell = Assert.Single(layer.CellsIn(new Rect(-100, -100, 200, 200), 0));

        Assert.NotNull(cell.Casing);
        Assert.Null(cell.Fill);
    }

    [Fact]
    public void Simplifying_keeps_the_ends_and_drops_points_closer_than_the_tolerance()
    {
        var points = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(2, 0, 0), new Vector3(10, 0, 0), new Vector3(11, 0, 0) };

        var kept = TrackLayer.Simplified(points, 5);

        Assert.Equal(new[] { 0f, 10f, 11f }, kept.Select(p => p.X));
    }
}
