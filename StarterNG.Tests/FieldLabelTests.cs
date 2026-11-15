using Avalonia.Automation;
using Avalonia.Controls;
using StarterNG.Controls;

namespace StarterNG.Tests;

public class FieldLabelTests
{
    static FieldLabelTests() => ScreenReaderNames.Install();

    private static T At<T>(T control, int row, int column) where T : Control
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        return control;
    }

    [Fact]
    public void A_field_is_named_by_the_nearest_label_left_of_it_in_its_row()
    {
        var far = At(new TextBlock { Text = "Pogoda" }, 0, 0);
        var near = At(new TextBlock { Text = "Temperatura" }, 0, 1);
        var value = At(new TextBlock { Text = "-5 °C" }, 0, 3);
        var other = At(new TextBlock { Text = "Widoczność" }, 1, 1);
        var slider = At(new Slider(), 0, 2);
        _ = new Grid { Children = { far, near, value, other, slider } };

        ScreenReaderNames.NameByLabel(slider);

        Assert.Equal("Temperatura", AutomationProperties.GetName(slider));
    }

    [Fact]
    public void A_field_with_nothing_to_its_left_is_named_by_the_label_above()
    {
        var label = At(new TextBlock { Text = "Klasa" }, 0, 1);
        var combo = At(new ComboBox(), 1, 1);
        _ = new Grid { Children = { label, combo } };

        Assert.Same(label, ScreenReaderNames.LabelOf(combo));
    }

    [Fact]
    public void In_a_panel_the_label_is_the_text_just_before_the_field()
    {
        var label = new TextBlock { Text = "Profil" };
        var box = new TextBox();
        var after = new TextBox();
        _ = new StackPanel { Children = { label, box, new Button(), after } };

        ScreenReaderNames.NameByLabel(box);

        Assert.Equal("Profil", AutomationProperties.GetName(box));
        Assert.Null(ScreenReaderNames.LabelOf(after));
    }

    [Fact]
    public void A_hidden_text_or_a_long_description_is_no_label()
    {
        var hidden = new TextBlock { Text = "Ukryte", IsVisible = false };
        var first = new ComboBox();
        var description = new TextBlock { Text = new string('x', 61) };
        var second = new ComboBox();
        _ = new StackPanel { Children = { hidden, first, description, second } };

        Assert.Null(ScreenReaderNames.LabelOf(first));
        Assert.Null(ScreenReaderNames.LabelOf(second));
        Assert.Null(ScreenReaderNames.LabelOf(new ComboBox()));
    }

    [Fact]
    public void The_name_follows_the_label_into_another_language()
    {
        var label = new TextBlock { Text = "Data" };
        var picker = new DatePicker();
        _ = new DockPanel { Children = { label, picker } };

        ScreenReaderNames.NameByLabel(picker);
        ScreenReaderNames.NameByLabel(picker);
        label.Text = "Date";

        Assert.Equal("Date", AutomationProperties.GetName(picker));
    }

    [Fact]
    public void A_name_given_on_purpose_beats_the_label()
    {
        var label = new TextBlock { Text = "Szukaj" };
        var box = new TextBox();
        AutomationProperties.SetName(box, "Szukaj pojazdów");
        _ = new StackPanel { Children = { label, box } };

        ScreenReaderNames.NameByLabel(box);

        Assert.Equal("Szukaj pojazdów", AutomationProperties.GetName(box));
    }
}
