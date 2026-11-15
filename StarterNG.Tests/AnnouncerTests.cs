using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using StarterNG.Controls;

namespace StarterNG.Tests;

[Collection(AvaloniaControls.Name)]
public class AnnouncerTests
{
    private DateTime _now = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    private Announcer NewAnnouncer() => new(() => _now);

    [Fact]
    public void A_message_is_read_while_fresh_and_then_forgotten()
    {
        var announcer = NewAnnouncer();
        announcer.Say(" Jakość grafiki: Wysoka ");
        Assert.Equal("Jakość grafiki: Wysoka", announcer.Message);

        _now += Announcer.Shown;
        Assert.Null(announcer.Message);
    }

    [Fact]
    public void Nothing_to_say_says_nothing()
    {
        var announcer = NewAnnouncer();
        announcer.Say("Zapisano ustawienia");
        announcer.Say("  ");

        Assert.Equal("Zapisano ustawienia", announcer.Message);
    }

    [Fact]
    public void A_control_inside_says_it_through_the_window_announcer()
    {
        var button = new Button();
        var announcer = NewAnnouncer();
        announcer.Child = new Border { Child = button };

        Announcer.Say(button, "Godzina: 07:05");

        Assert.Equal("Godzina: 07:05", announcer.Message);
    }

    [Fact]
    public void A_control_outside_any_announcer_is_not_heard()
    {
        var button = new Button();

        var thrown = Record.Exception(() => Announcer.Say(button, "Godzina: 07:05"));

        Assert.Null(thrown);
        Assert.Null(button.FindAncestorOfType<Announcer>());
    }

    [Fact]
    public void A_marked_text_is_read_when_it_changes()
    {
        var status = new TextBlock();
        Announcer.SetChanges(status, true);
        var plain = new TextBlock();
        var announcer = NewAnnouncer();
        announcer.Child = new StackPanel { Children = { status, plain } };

        plain.Text = "Ścieżka";
        Assert.Null(announcer.Message);

        status.Text = "Niezapisane zmiany";
        Assert.True(Announcer.GetChanges(status));
        Assert.Equal("Niezapisane zmiany", announcer.Message);
    }

    [Fact]
    public void A_screen_reader_hears_it_as_a_polite_live_region()
    {
        var announcer = NewAnnouncer();
        var peer = ControlAutomationPeer.CreatePeerForElement(announcer);
        announcer.Say("Przywrócono domyślne");

        Assert.Equal("Przywrócono domyślne", peer.GetName());
        Assert.Equal(AutomationLiveSetting.Polite, peer.GetLiveSetting());
        Assert.Equal(AutomationControlType.Group, peer.GetAutomationControlType());
    }
}

[Collection(AvaloniaControls.Name)]
public class NamedGroupTests
{
    static NamedGroupTests() => ScreenReaderNames.Install();

    [Fact]
    public void A_panel_with_a_title_is_a_group_named_by_it()
    {
        var panel = new HeaderedContentControl { Header = "Scenerie" };

        Assert.Equal("Scenerie", AutomationProperties.GetName(panel));
        Assert.Equal(AutomationControlType.Group, AutomationProperties.GetControlTypeOverride(panel));
        Assert.Equal(AccessibilityView.Content, AutomationProperties.GetAccessibilityView(panel));
    }

    [Fact]
    public void A_panel_whose_title_goes_is_no_longer_a_group()
    {
        var panel = new HeaderedContentControl { Header = "Skład" };
        panel.Header = null;

        Assert.Null(AutomationProperties.GetName(panel));
        Assert.Null(AutomationProperties.GetControlTypeOverride(panel));
        Assert.Equal(AccessibilityView.Default, AutomationProperties.GetAccessibilityView(panel));
    }

    [Fact]
    public void A_tab_keeps_its_own_way_of_speaking()
    {
        var tab = new TabItem { Header = "Pogoda" };

        Assert.Null(AutomationProperties.GetName(tab));
        Assert.Equal(AutomationControlType.TabItem, AutomationProperties.GetControlTypeOverride(tab));
    }
}
