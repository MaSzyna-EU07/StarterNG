using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.VisualTree;
using StarterNG.Controls;

namespace StarterNG.Tests;

/// <summary>
/// Windows opened over another, searches and the starter's own controls as someone at the
/// keyboard with a screen reader meets them, in windows built as the application builds them.
/// </summary>
[Collection(AvaloniaControls.Name)]
public class DialogControlsTests
{
    static DialogControlsTests()
    {
        HeadlessDrawing.Ensure();
        DialogKeyboard.Install();
        ScreenReaderNames.Install();
    }

    private static (Window Owner, Window Dialog) Dialog(Control content, bool escapeCloses = true)
    {
        var owner = new Window();
        owner.Show();
        var dialog = new Window { Width = 300, Height = 200, Content = content };
        DialogKeyboard.SetEscapeCloses(dialog, escapeCloses);
        _ = dialog.ShowDialog(owner);
        // Headless a window says it opened before it is given its owner; it says so again once it has one.
        Assert.Same(owner, dialog.Owner);
        dialog.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Window.WindowOpenedEvent));
        Controls.Settle();
        return (owner, dialog);
    }

    // --- windows over another ---

    [Fact]
    public void A_dialog_opens_on_its_default_button_and_esc_closes_it()
    {
        var other = new Button { Content = "Nie" };
        var yes = new Button { Content = "Tak", IsDefault = true };
        var (owner, dialog) = Dialog(new StackPanel { Children = { other, yes } });
        bool closed = false;
        dialog.Closed += (_, _) => closed = true;

        Assert.True(yes.IsFocused);
        Assert.False(Controls.Press(dialog, Key.Escape, KeyModifiers.Shift));
        Assert.True(Controls.Press(dialog, Key.Escape));
        Assert.True(closed);
        owner.Close();
    }

    [Fact]
    public void Without_a_default_button_focus_goes_to_the_first_control()
    {
        var first = new TextBox();
        var (owner, dialog) = Dialog(new StackPanel { Children = { first, new Button { Content = "OK" } } });

        Assert.True(first.IsFocused);
        dialog.Close();
        owner.Close();
    }

    [Fact]
    public void A_window_that_placed_the_focus_itself_keeps_it()
    {
        var first = new Button { Content = "1" };
        var chosen = new Button { Content = "2" };
        var (owner, dialog) = Dialog(new StackPanel { Children = { first, chosen } });
        chosen.Focus();
        Assert.False(first.IsFocused);

        DialogKeyboard.FocusFirst(dialog);

        Assert.True(chosen.IsFocused);
        dialog.Close();
        owner.Close();
    }

    [Fact]
    public void Esc_does_not_close_a_window_that_stays_beside_the_main_one()
    {
        var (owner, dialog) = Dialog(new Button { Content = "OK" }, escapeCloses: false);
        bool closed = false;
        dialog.Closed += (_, _) => closed = true;

        Assert.False(DialogKeyboard.GetEscapeCloses(dialog));
        Assert.False(Controls.Press(dialog, Key.Escape));
        Assert.False(closed);
        dialog.Close();
        owner.Close();
    }

    [Fact]
    public void Tab_with_only_the_window_focused_starts_from_the_first_control()
    {
        var first = new Button { Content = "1" };
        // Opened with nothing to focus, its controls put in afterwards.
        var (owner, dialog) = Dialog(new TextBlock { Text = "…" });
        dialog.Content = new StackPanel { Children = { first, new Button { Content = "2" } } };
        dialog.UpdateLayout();
        Assert.False(first.IsFocused);

        Assert.True(Controls.Press(dialog, Key.Tab));
        Assert.True(first.IsFocused);
        dialog.Close();
        owner.Close();
    }

    [Fact]
    public void A_window_of_its_own_is_left_to_its_own_ways()
    {
        var button = new Button { Content = "1" };
        var window = Controls.Show(button);

        Assert.False(Controls.Press(window, Key.Escape));
        window.Close();
    }

    // --- searches ---

    [Fact]
    public void A_search_says_how_many_entries_are_left_once_typing_pauses()
    {
        var box = new TextBox();
        var announcer = new Announcer { Child = box };
        var window = Controls.Show(announcer);
        try
        {
            var teller = new SearchResultsTeller(box, () => 3);
            box.Focus();
            box.Text = "EU07";
            teller.Changed();
            Thread.Sleep(SearchResultsTeller.Pause + TimeSpan.FromMilliseconds(300));
            Controls.Settle();

            Assert.NotNull(announcer.Message);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void An_empty_search_or_one_left_says_nothing()
    {
        var box = new TextBox();
        var elsewhere = new Button { Content = "x" };
        var announcer = new Announcer { Child = new StackPanel { Children = { box, elsewhere } } };
        var window = Controls.Show(announcer);
        try
        {
            var teller = new SearchResultsTeller(box, () => 3);
            box.Focus();
            box.Text = " ";
            teller.Changed();
            Thread.Sleep(SearchResultsTeller.Pause + TimeSpan.FromMilliseconds(300));
            Controls.Settle();
            Assert.Null(announcer.Message);

            box.Text = "EU07";
            elsewhere.Focus();
            teller.Changed();
            Thread.Sleep(SearchResultsTeller.Pause + TimeSpan.FromMilliseconds(300));
            Controls.Settle();
            Assert.Null(announcer.Message);
        }
        finally
        {
            window.Close();
        }
    }

    // --- tree entries ---

    [Fact]
    public void Every_part_of_a_row_tells_whether_the_entry_is_open()
    {
        var words = ExpandableTreeViewItem.StateWords;
        ExpandableTreeViewItem.StateWords = s => s switch
        {
            ExpandCollapseState.Expanded => "rozwiniÄ™te",
            ExpandCollapseState.Collapsed => "zwiniÄ™te",
            _ => null
        };
        var child = new ExpandableTreeViewItem { Header = "test32" };
        var group = new ExpandableTreeViewItem { Header = "CaĹ‚kowo", Items = { child } };
        AutomationProperties.SetName(group, "CaĹ‚kowo");
        var tree = new TreeView { Items = { group } };
        var window = Controls.Show(tree, themed: true);
        try
        {
            var row = group.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PART_LayoutRoot");
            Assert.Equal("zwiniÄ™te", AutomationProperties.GetHelpText(row));
            Assert.Equal("CaĹ‚kowo", AutomationProperties.GetName(row));

            var peer = Controls.Peer(group);
            ((IExpandCollapseProvider)peer).Expand();
            Controls.Settle();
            Assert.Equal("rozwiniÄ™te", AutomationProperties.GetHelpText(row));

            AutomationProperties.SetName(group, "CaĹ‚kowo i okolice");
            Assert.Equal("CaĹ‚kowo i okolice", AutomationProperties.GetName(row));

            group.Header = "CaĹ‚kowo 2";
            Controls.Settle();
            window.UpdateLayout();
            Assert.Same(group, child.ReadableParent);
            Assert.Same(peer, Controls.Peer(child).GetParent());

            var leafRow = child.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PART_LayoutRoot");
            Assert.Null(AutomationProperties.GetHelpText(leafRow));

            tree.Items.Clear();
            Controls.Settle();
        }
        finally
        {
            ExpandableTreeViewItem.StateWords = words;
            window.Close();
        }
    }

    // --- sliders and number boxes ---

    [Fact]
    public void The_track_and_knob_of_a_slider_are_named_with_its_value()
    {
        var fog = new ReadableSlider { Minimum = 0, Maximum = 1000, Value = 500, ValueText = "2,4 km" };
        AutomationProperties.SetName(fog, "WidocznoĹ›Ä‡");
        var window = Controls.Show(fog, themed: true);
        try
        {
            var track = fog.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Track>().Single();
            Assert.Equal("WidocznoĹ›Ä‡: 2,4 km", AutomationProperties.GetName(track));

            fog.ValueText = "3 km";
            Assert.Equal("WidocznoĹ›Ä‡: 3 km", AutomationProperties.GetName(track));

            var value = Assert.IsAssignableFrom<IValueProvider>(Controls.Peer(fog));
            Assert.False(value.IsReadOnly);
            Assert.Equal("Slider", Controls.Peer(fog).GetClassName());
            Assert.Throws<ArgumentException>(() => value.SetValue("duĹĽo"));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void A_number_box_gives_its_name_to_the_text_inside_and_its_arrows_say_what_they_do()
    {
        var box = new NumericUpDown { Value = 3 };
        AutomationProperties.SetName(box, "Liczba wagonĂłw");
        var window = Controls.Show(box, themed: true);
        try
        {
            var text = box.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.Equal("Liczba wagonĂłw", AutomationProperties.GetName(text));

            var arrows = box.GetVisualDescendants().OfType<Control>()
                            .Where(c => c.Name is "PART_IncreaseButton" or "PART_DecreaseButton").ToList();
            Assert.NotEmpty(arrows);
            Assert.All(arrows, a => Assert.False(KeyboardNavigation.GetIsTabStop(a)));
            Assert.All(arrows, a => Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(a))));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void A_picker_is_reached_through_its_button_named_with_the_label_and_value()
    {
        var date = new DatePicker { SelectedDate = new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero) };
        var time = new TimePicker { SelectedTime = new TimeSpan(7, 5, 0) };
        AutomationProperties.SetName(date, "Data");
        AutomationProperties.SetName(time, "Godzina");
        var window = Controls.Show(new StackPanel { Children = { date, time } }, themed: true);
        try
        {
            var dateButton = date.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_FlyoutButton");
            var timeButton = time.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_FlyoutButton");
            Assert.StartsWith("Data", AutomationProperties.GetName(dateButton));
            Assert.StartsWith("Godzina", AutomationProperties.GetName(timeButton));

            time.SelectedTime = new TimeSpan(8, 0, 0);
            Assert.StartsWith("Godzina", AutomationProperties.GetName(timeButton));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void A_list_says_its_new_choice_when_the_arrows_change_it()
    {
        var combo = new ComboBox { ItemsSource = new[] { "Niska", "Ĺšrednia", "Wysoka" }, SelectedIndex = 0 };
        var announcer = new Announcer { Child = combo };
        var window = Controls.Show(announcer, themed: true);
        try
        {
            combo.Focus();
            combo.SelectedIndex = 2;

            Assert.Equal("Wysoka", announcer.Message);
        }
        finally
        {
            window.Close();
        }
    }

    // --- settings sections ---

    private static SettingsSection Section() => new()
    {
        Template = new FuncControlTemplate<SettingsSection>((_, scope) =>
        {
            var reset = new Button { Name = "PART_Reset" };
            scope.Register(reset.Name, reset);
            return reset;
        })
    };

    [Fact]
    public void A_sections_reset_button_says_which_section_it_puts_back()
    {
        var section = Section();
        section.Header = "Grafika";
        section.ResetLabel = "PrzywrĂłÄ‡";
        SettingsSection? asked = null;
        section.ResetRequested += s => asked = s;
        Controls.Lay(section);
        section.ApplyTemplate();
        var reset = (Button)section.GetVisualChildren().Single();

        Assert.Equal("PrzywrĂłÄ‡: Grafika", AutomationProperties.GetName(reset));
        section.Header = "DĹşwiÄ™k";
        Assert.Equal("PrzywrĂłÄ‡: DĹşwiÄ™k", AutomationProperties.GetName(reset));

        section.ResetLabel = null;
        section.Header = null;
        Assert.True(string.IsNullOrEmpty(AutomationProperties.GetName(reset)));

        reset.RaiseEvent(Controls.Click());
        Assert.Same(section, asked);

        section.Template = Section().Template;
        section.ApplyTemplate();
        Assert.Null(section.ToggleKey);
        Assert.Null(section.Toggle);
    }

    [Fact]
    public void A_sections_list_of_levels_is_named_as_the_level_of_that_section()
    {
        var level = new ComboBox();
        var section = new SettingsSection { Level = level, Header = "Grafika", ToggleKey = "shadows", Toggle = new CheckBox() };

        Assert.EndsWith("Grafika", AutomationProperties.GetName(level));
        Assert.Same(level, section.Level);
        Assert.Equal("shadows", section.ToggleKey);
    }
}
