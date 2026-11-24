using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Eu07.BlackBox;
using Eu07.BlackBox.Views;

namespace StarterNG.Tests.BlackBox;

/// <summary>
/// The black box's own drawn panels as a screen reader user meets them: reached with Tab,
/// walked with the keys, their value and description read, what they show said aloud.
/// </summary>
[Collection(AvaloniaControls.Name)]
public class BlackBoxViewsTests
{
    private const uint Head = 1;

    /// <summary>
    /// The user in vehicle 1 from t = 0 to 120: timetable ROJ1 standing at A, stopping at B
    /// from 60 to 70, passing C at 100; the clock at 10:34 (634 min).
    /// </summary>
    private static Recording Journey(bool extended = true, byte? engine = null)
    {
        var f = new BlackBoxFile().Info()
            .Vehicle(Head, "EU07-005", "ROJ1")
            .User(0, Head, true, 1)
            .Timetable(0, Head, "ROJ1", "A", "C", ("A", -1, 634), ("Stacja_B", 635, 635.5f), ("C", 637.5f, 638))
            .Station(1, Head, 4, 1, "A", 1150);
        for (double t = 0; t <= 120; t += 0.5)
        {
            float speed = t is >= 55 and < 70 ? 0 : 40;
            f.Cab(t, Head, 1150 + t * 10, speed, extended: extended, engine: engine, nextLimit: 60, nextDistance: 500,
                  brakeCylinder: t is >= 50 and < 70 ? 2.5f : 0, devices: 0x0207);
        }
        f.Station(60, Head, 1, 2, "Stacja_B", 1300)
         .Station(100, Head, 3, 3, "C", 2150);
        return BlackBoxReader.Read(f.End(120).Stream());
    }

    private static IValueProvider Value(Control control) =>
        Assert.IsAssignableFrom<IValueProvider>(Controls.Peer(control));

    // --- timeline ---

    [Fact]
    public void The_timeline_is_a_slider_read_as_the_moment_with_how_to_walk_it()
    {
        var timeline = new TimelineView { Spoken = "10:35:12, 40 km/h" };
        var peer = Controls.Peer(timeline);
        var value = Value(timeline);

        Assert.Equal(AutomationControlType.Slider, peer.GetAutomationControlType());
        Assert.Equal("10:35:12, 40 km/h", value.Value);
        Assert.True(value.IsReadOnly);
        Assert.False(string.IsNullOrEmpty(peer.GetHelpText()));
        Assert.True(peer.IsContentElement());
        Assert.True(peer.IsControlElement());
        Assert.Throws<InvalidOperationException>(() => value.SetValue("x"));

        timeline.Spoken = "10:35:22, 40 km/h";
        Assert.Equal("10:35:22, 40 km/h", value.Value);
    }

    [Fact]
    public void A_description_set_on_the_timeline_wins_over_its_own()
    {
        var timeline = new TimelineView();
        AutomationProperties.SetHelpText(timeline, "own words");

        Assert.Equal("own words", Controls.Peer(timeline).GetHelpText());
    }

    [Theory]
    [InlineData(Key.Right, KeyModifiers.None, 70)]
    [InlineData(Key.Left, KeyModifiers.None, 50)]
    [InlineData(Key.Right, KeyModifiers.Control, 120)]
    [InlineData(Key.Left, KeyModifiers.Control, 0)]
    [InlineData(Key.PageDown, KeyModifiers.None, 120)]
    [InlineData(Key.PageUp, KeyModifiers.None, 0)]
    [InlineData(Key.Home, KeyModifiers.None, 0)]
    [InlineData(Key.End, KeyModifiers.None, 120)]
    public void The_keys_take_the_timeline_through_the_recording(Key key, KeyModifiers modifiers, double expected)
    {
        var timeline = new TimelineView { Recording = Journey(), Time = 60 };
        double? sought = null;
        timeline.Seek += t => sought = t;

        Assert.True(Controls.Press(timeline, key, modifiers));
        Assert.Equal(expected, sought);
    }

    [Fact]
    public void Other_keys_and_an_empty_timeline_are_left_alone()
    {
        var empty = new TimelineView();
        var timeline = new TimelineView { Recording = Journey(), Time = 60 };
        bool sought = false;
        empty.Seek += _ => sought = true;
        timeline.Seek += _ => sought = true;

        Assert.False(Controls.Press(empty, Key.Right));
        Assert.False(Controls.Press(timeline, Key.A));
        Assert.False(sought);
    }

    [Fact]
    public void The_timeline_draws_with_and_without_the_focus()
    {
        var timeline = new TimelineView { Recording = Journey(), Time = 60 };
        var window = Controls.Show(timeline, 900, 300);
        try
        {
            timeline.Fit();
            Controls.Draw(timeline);
            Assert.True(timeline.IsFocused);
            timeline.Time = 119;
            Controls.Draw(timeline);
            window.Focus();
            Controls.Draw(timeline);
        }
        finally
        {
            window.Close();
        }
    }

    // --- timetable ---

    [Fact]
    public void The_timetable_is_a_list_read_row_by_row_from_the_keys()
    {
        var table = new TimetablePanel { Recording = Journey(), Time = 30 };
        var peer = Controls.Peer(table);
        var value = Value(table);

        Assert.Equal(AutomationControlType.List, peer.GetAutomationControlType());
        Assert.False(string.IsNullOrEmpty(peer.GetHelpText()));
        Assert.StartsWith("1/3. A", value.Value);

        Assert.True(Controls.Press(table, Key.Down));
        Assert.StartsWith("2/3. Stacja B", value.Value);
        Assert.True(Controls.Press(table, Key.Up));
        Assert.StartsWith("1/3. A", value.Value);
        Assert.True(Controls.Press(table, Key.End));
        Assert.StartsWith("3/3. C", value.Value);
        Assert.True(Controls.Press(table, Key.Down));
        Assert.StartsWith("3/3. C", value.Value);
        Assert.True(Controls.Press(table, Key.Home));
        Assert.StartsWith("1/3. A", value.Value);
        Assert.Contains("km 2.5", value.Value.Replace(',', '.'));
        Assert.False(Controls.Press(table, Key.A));
    }

    [Fact]
    public void Enter_on_a_station_passed_goes_to_a_little_before_it()
    {
        var table = new TimetablePanel { Recording = Journey(), Time = 110 };
        double? sought = null;
        table.Seek += t => sought = t;

        Controls.Press(table, Key.Home);
        Controls.Press(table, Key.Down);

        Assert.True(Controls.Press(table, Key.Enter));
        Assert.Equal(55, sought);
    }

    [Fact]
    public void A_recording_without_a_timetable_says_so_and_ignores_the_keys()
    {
        var recording = BlackBoxReader.Read(new BlackBoxFile().Info().Vehicle(Head, "EU07-005")
                                                .Cab(0, Head, 1150, 40).End(1).Stream());
        var table = new TimetablePanel { Recording = recording, Time = 0 };

        Assert.False(string.IsNullOrEmpty(Value(table).Value));
        Assert.False(Controls.Press(table, Key.Down));
        table.Recording = null;
        Controls.Lay(table);
        Controls.Draw(table);
    }

    [Fact]
    public void The_timetable_draws_its_rows_and_the_one_the_keys_stand_at()
    {
        var table = new TimetablePanel { Recording = Journey(), Time = 65 };
        var window = Controls.Show(table, 600, 400);
        try
        {
            Controls.Draw(table);
            Controls.Press(table, Key.Down);
            Controls.Draw(table);
            window.Focus();
            Controls.Draw(table);
        }
        finally
        {
            window.Close();
        }
    }

    // --- cab ---

    [Theory]
    [InlineData(true, null)]
    [InlineData(true, (byte)1)]
    [InlineData(true, (byte)2)]
    [InlineData(false, null)]
    public void The_cab_says_what_it_draws(bool extended, byte? engine)
    {
        var cab = new CabPanel { Recording = Journey(extended, engine), Time = 30 };
        Controls.Lay(cab, 600, 900);

        Controls.Draw(cab);

        Assert.NotNull(cab.Said);
        Assert.Contains("EU07-005", cab.Said);
        Assert.Contains("40 km/h", cab.Said);
        Assert.Equal(AutomationControlType.Pane, Controls.Peer(cab).GetAutomationControlType());
        Assert.Equal(cab.Said, Controls.Peer(cab).GetHelpText());
    }

    [Fact]
    public void Wheels_turning_apart_from_the_train_are_said_beside_its_speed()
    {
        var f = new BlackBoxFile().Info()
            .Vehicle(Head, "EU07-005", "ROJ1")
            .User(0, Head, true, 1);
        // slipping for the first ten seconds, then the wheels as fast as the train, give or take the rounding
        for (double t = 0; t <= 20; t += 0.5)
            f.Cab(t, Head, 1150 + t * 10, 40, movement: 1, equippedHead: 0x15, equippedTail: 0x22, engine: 1,
                  wheelSpeed: t < 10 ? 55.4f : 40.3f);
        var cab = new CabPanel
        {
            Recording = BlackBoxReader.Read(f.End(20).Stream()),
            Strings = new DefaultBlackBoxStrings("en"),
            Time = 5
        };
        Controls.Lay(cab, 600, 900);

        Controls.Draw(cab);
        Assert.Contains("40 km/h", cab.Said);
        Assert.Contains("wheels 55", cab.Said);

        cab.Time = 15;
        Controls.Draw(cab);
        Assert.Contains("40 km/h", cab.Said);
        Assert.DoesNotContain("wheels", cab.Said);
    }

    [Fact]
    public void The_cab_stopped_under_the_brakes_and_out_of_the_recording()
    {
        var cab = new CabPanel { Recording = Journey(), Time = 60 };
        var window = Controls.Show(cab, 600, 900);
        try
        {
            Controls.Draw(cab);
            Assert.Contains("0 km/h", cab.Said);

            cab.Time = 500;
            Controls.Draw(cab);
            Assert.DoesNotContain("km/h", cab.Said);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void An_empty_cab_says_there_is_nothing_to_show()
    {
        var cab = new CabPanel();
        Controls.Lay(cab);

        Controls.Draw(cab);

        Assert.False(string.IsNullOrEmpty(cab.Said));
    }

    // --- segmented choice ---

    [Fact]
    public void A_segmented_choice_is_a_row_of_tabs_one_of_them_selected()
    {
        var choice = new UiKit.Segmented("Mapa", "Profil", "Rozkład");
        int? picked = null;
        choice.SelectedChanged += i => picked = i;
        var tabs = choice.GetVisualDescendants().OfType<Button>().ToList();
        if (tabs.Count == 0)
            tabs = ((Panel)choice.Child!).Children.OfType<Button>().ToList();

        var second = Assert.IsAssignableFrom<ISelectionItemProvider>(Controls.Peer(tabs[1]));
        Assert.Equal(AutomationControlType.TabItem, Controls.Peer(tabs[1]).GetAutomationControlType());
        Assert.Null(second.SelectionContainer);

        second.Select();
        Assert.Equal(1, picked);
        Assert.True(second.IsSelected);
        Assert.Equal(1, choice.Selected);

        var third = Assert.IsAssignableFrom<ISelectionItemProvider>(Controls.Peer(tabs[2]));
        third.AddToSelection();
        Assert.Equal(2, choice.Selected);
        Assert.False(second.IsSelected);
        third.RemoveFromSelection();
        Assert.Equal(2, choice.Selected);
    }
}
