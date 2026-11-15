using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Input;
using StarterNG.Application;
using StarterNG.Domain.Tapes;
using StarterNG.Tests.Fakes;
using StarterNG.Views.Tapes;

namespace StarterNG.Tests;

/// <summary>A recorded trip walked from the keyboard and read by a screen reader, on both ways of drawing it.</summary>
[Collection(AvaloniaControls.Name)]
public class TapeViewTests
{
    // The electronic tape writes its legend in the starter's language.
    static TapeViewTests()
    {
        if (AppServices.CurrentOrNull is null)
        {
            var installation = new TestInstallation(new InMemoryFileSystem());
            AppServices.Initialize(installation.Paths, installation.Files, installation.Clock, installation.Environment);
        }
    }

    public static TheoryData<string> Views => new() { "hasler", "electronic" };

    private static TapeView View(string kind) => kind == "hasler" ? new HaslerTapeView() : new ElectronicTapeView();

    /// <summary>Two minutes, a sample a second, 15 m a second; running from 10 s to 110 s, braking after 100 s.</summary>
    private static HaslerTape Trip() =>
        new(Enumerable.Range(0, 121)
                      .Select(i => new TapeSample(i, i is > 10 and < 110 ? 54 : 0, i * 15.0, i > 100 ? 2 : 0, 5,
                                                  i < 50 ? 300 : 0, i == 70))
                      .ToList());

    private static (TapeView View, List<TapeSample?> Hovered) Walking(string kind, bool byDistance = true)
    {
        var view = View(kind);
        view.Tape = Trip();
        view.ByDistance = byDistance;
        var hovered = new List<TapeSample?>();
        view.Hovered += s => hovered.Add(s);
        return (view, hovered);
    }

    [Theory]
    [MemberData(nameof(Views))]
    public void The_trip_is_a_slider_read_as_the_moment_under_the_cursor(string kind)
    {
        var view = View(kind);
        var peer = Controls.Peer(view);
        var value = Assert.IsAssignableFrom<IValueProvider>(peer);

        view.Spoken = "10:00:12, km 0,2, 54 km/h";

        Assert.Equal(AutomationControlType.Slider, peer.GetAutomationControlType());
        Assert.True(peer.IsContentElement());
        Assert.True(peer.IsControlElement());
        Assert.Equal("10:00:12, km 0,2, 54 km/h", value.Value);
        Assert.True(value.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => value.SetValue("x"));

        view.Spoken = "10:00:13, km 0,2, 54 km/h";
        Assert.Equal("10:00:13, km 0,2, 54 km/h", value.Value);
    }

    [Theory]
    [MemberData(nameof(Views))]
    public void The_first_arrow_starts_at_the_beginning_and_the_next_go_a_hundred_metres(string kind)
    {
        var (view, hovered) = Walking(kind);

        Assert.True(Controls.Press(view, Key.Right));
        Assert.Equal(0, hovered[^1]!.Value.Time);

        Assert.True(Controls.Press(view, Key.Right));
        Assert.InRange(hovered[^1]!.Value.DistanceM, TapeView.DistanceStep - 15, TapeView.DistanceStep + 15);

        Assert.True(Controls.Press(view, Key.Right, KeyModifiers.Control));
        Assert.True(hovered[^1]!.Value.DistanceM >= TapeView.DistanceStep * TapeView.CtrlTimes - 15);

        Assert.True(Controls.Press(view, Key.Left, KeyModifiers.Control));
        Assert.True(hovered[^1]!.Value.DistanceM <= TapeView.DistanceStep + 15);

        Assert.True(Controls.Press(view, Key.End));
        Assert.Equal(120, hovered[^1]!.Value.Time);
        Assert.True(Controls.Press(view, Key.Right));
        Assert.Equal(120, hovered[^1]!.Value.Time);

        Assert.True(Controls.Press(view, Key.Home));
        Assert.Equal(0, hovered[^1]!.Value.Time);
        Assert.True(Controls.Press(view, Key.Left));
        Assert.Equal(0, hovered[^1]!.Value.Time);
    }

    [Theory]
    [MemberData(nameof(Views))]
    public void By_time_an_arrow_goes_ten_seconds(string kind)
    {
        var (view, hovered) = Walking(kind, byDistance: false);

        Controls.Press(view, Key.Left);
        Assert.Equal(0, hovered[^1]!.Value.Time);
        Controls.Press(view, Key.Right);
        Assert.Equal(TapeView.TimeStep, hovered[^1]!.Value.Time);
        Controls.Press(view, Key.Left);
        Assert.Equal(0, hovered[^1]!.Value.Time);
    }

    [Theory]
    [MemberData(nameof(Views))]
    public void Enter_picks_the_sample_the_keys_stand_at_once_they_have_moved(string kind)
    {
        var (view, _) = Walking(kind);
        TapeSample? picked = null;
        view.Picked += s => picked = s;

        Assert.False(Controls.Press(view, Key.Enter));
        Assert.Null(picked);

        Controls.Press(view, Key.End);
        Assert.True(Controls.Press(view, Key.Enter));
        Assert.Equal(120, picked!.Value.Time);
    }

    [Theory]
    [MemberData(nameof(Views))]
    public void A_new_trip_starts_the_keys_from_its_beginning(string kind)
    {
        var (view, hovered) = Walking(kind);
        Controls.Press(view, Key.End);

        view.Tape = Trip();
        Controls.Press(view, Key.Right);

        Assert.Equal(0, hovered[^1]!.Value.Time);
    }

    [Theory]
    [MemberData(nameof(Views))]
    public void Other_keys_and_an_empty_trip_are_left_alone(string kind)
    {
        var (view, hovered) = Walking(kind);
        var empty = View(kind);
        var blank = View(kind);
        blank.Tape = new HaslerTape([]);

        Assert.False(Controls.Press(view, Key.A));
        Assert.False(Controls.Press(empty, Key.Right));
        Assert.False(Controls.Press(blank, Key.Right));
        Assert.Empty(hovered);
    }

    [Theory]
    [MemberData(nameof(Views))]
    public void The_trip_draws_with_and_without_the_focus(string kind)
    {
        var (view, _) = Walking(kind);
        view.Mark = 60;
        var window = Controls.Show(view, 1200, 600);
        try
        {
            Assert.True(view.Focusable);
            view.Focus();
            Controls.Press(view, Key.Right);
            Controls.Settle();
            window.Focus();
            Controls.Settle();
        }
        finally
        {
            window.Close();
        }
    }
}
