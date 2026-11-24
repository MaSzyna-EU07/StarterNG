using Avalonia.Automation;
using Avalonia.Controls;
using StarterNG.Controls;
using StarterNG.Infrastructure;

namespace StarterNG.Tests;

[Collection(AvaloniaControls.Name)]
public class MouseReadingTests
{
    static MouseReadingTests() => ScreenReaderNames.Install();

    [Fact]
    public void A_picture_on_a_button_is_read_as_the_button()
    {
        var logo = new Image();
        var button = new Button { Content = logo };

        ScreenReaderNames.NamePicture(logo);

        Assert.Same(button, AutomationProperties.GetLabeledBy(logo));
    }

    [Fact]
    public void A_picture_beside_a_caption_is_read_as_the_caption()
    {
        var mini = new Image();
        var caption = new TextBlock { Text = "EU07" };
        _ = new StackPanel { Children = { mini, new TextBlock { IsVisible = false, Text = "ukryte" }, caption } };

        ScreenReaderNames.NamePicture(mini);

        Assert.Same(caption, AutomationProperties.GetLabeledBy(mini));
    }

    [Fact]
    public void A_picture_in_a_card_is_read_as_the_card_tooltip()
    {
        var mini = new Image();
        var card = new Border { Child = new Panel { Children = { mini } } };
        ToolTip.SetTip(card, "EP09-018");

        ScreenReaderNames.NamePicture(mini);

        Assert.Equal("EP09-018", AutomationProperties.GetName(mini));
    }

    [Fact]
    public void A_picture_named_on_purpose_or_far_from_any_name_is_left_alone()
    {
        var named = new Image();
        AutomationProperties.SetName(named, "Obraz scenerii: baltyk");
        var far = new Image();
        var outer = new Border { Child = new Border { Child = new Border { Child = new Border { Child = far } } } };
        ToolTip.SetTip(outer, "Opis");
        _ = new Button { Content = named };

        ScreenReaderNames.NamePicture(named);
        ScreenReaderNames.NamePicture(far);

        Assert.Equal("Obraz scenerii: baltyk", AutomationProperties.GetName(named));
        Assert.Null(AutomationProperties.GetLabeledBy(named));
        Assert.Null(AutomationProperties.GetName(far));
    }

    [Fact]
    public void A_list_entry_is_read_as_its_text()
    {
        var named = new ComboBoxItem { Content = "Lato" };
        AutomationProperties.SetName(named, "Pora: lato");

        Assert.Equal("Pogodnie", ScreenReaderNames.SpokenItem(new ComboBoxItem { Content = "Pogodnie" }));
        Assert.Equal("Pora: lato", ScreenReaderNames.SpokenItem(named));
        Assert.Equal("12", ScreenReaderNames.SpokenItem(12));
        Assert.Null(ScreenReaderNames.SpokenItem(null));
    }

    [Fact]
    public void A_list_not_in_focus_says_nothing_of_its_new_choice()
    {
        var combo = new ComboBox();
        var announcer = new Announcer();
        announcer.Child = combo;

        ScreenReaderNames.TellChoice(combo);

        Assert.Null(announcer.Message);
    }

    [Fact]
    public void A_figure_says_its_label_and_its_value_together()
    {
        var host = new StackPanel();

        StatsBar.Fill(host, new[] { ("Masa [t]:", "440"), ("Tor:", "tor0325") });

        var figure = Assert.IsType<TextBlock>(host.Children[0]);
        Assert.Equal("Masa [t]: 440", figure.Inlines!.Text);
        Assert.Equal(22, figure.Margin.Right);
        Assert.Equal(0, ((TextBlock)host.Children[1]).Margin.Right);
    }
}
