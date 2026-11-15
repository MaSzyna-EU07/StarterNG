using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using StarterNG.Controls;

namespace StarterNG.Tests;

public class ScreenReaderNamesTests
{
    static ScreenReaderNamesTests() => ScreenReaderNames.Install();

    private static StackPanel Row(params Control[] children)
    {
        var panel = new StackPanel();
        panel.Children.AddRange(children);
        return panel;
    }

    [Fact]
    public void An_icon_button_is_named_by_its_tooltip()
    {
        var button = new Button { Content = new Image() };
        ToolTip.SetTip(button, "Otwórz ostatni log symulatora");

        Assert.Equal("Otwórz ostatni log symulatora", AutomationProperties.GetName(button));
    }

    [Fact]
    public void A_button_without_a_tooltip_is_named_by_the_text_it_shows()
    {
        var button = new Button { Content = Row(new Image(), new TextBlock { Text = "Dodaj" }) };

        Assert.Equal("Dodaj", AutomationProperties.GetName(button));
    }

    [Fact]
    public void A_button_with_text_of_its_own_keeps_it()
    {
        var button = new Button { Content = "Zapisz" };
        ToolTip.SetTip(button, "Zapisz ustawienia");

        Assert.Null(AutomationProperties.GetName(button));
    }

    [Fact]
    public void A_name_given_on_purpose_is_left_alone()
    {
        var button = new Button();
        AutomationProperties.SetName(button, "Discord");
        button.Content = new Image();
        ToolTip.SetTip(button, "Dołącz do nas");

        Assert.Equal("Discord", AutomationProperties.GetName(button));
    }

    [Fact]
    public void A_new_tooltip_replaces_the_name_it_gave()
    {
        var button = new Button { Content = new Image() };
        ToolTip.SetTip(button, "Undo");
        ToolTip.SetTip(button, "Cofnij");

        Assert.Equal("Cofnij", AutomationProperties.GetName(button));
    }

    [Fact]
    public void Text_put_on_a_button_later_takes_back_the_name_it_was_given()
    {
        var button = new Button { Content = new Image() };
        ToolTip.SetTip(button, "Pomoc");
        button.Content = "Pomoc";

        Assert.Null(AutomationProperties.GetName(button));
    }

    [Fact]
    public void A_button_with_only_a_sign_on_it_is_named_by_its_tooltip()
    {
        var button = new Button { Content = "?" };
        ToolTip.SetTip(button, "Pomoc");

        Assert.Equal("Pomoc", AutomationProperties.GetName(button));
    }

    [Fact]
    public void A_tree_entry_is_named_by_the_visible_text_of_its_header()
    {
        var hidden = new TextBlock { Text = "ukryte", IsVisible = false };
        var item = new TreeViewItem
        {
            Header = Row(new Image(), new TextBlock { Text = "baltyk_cargo" }, hidden, new TextBlock { Text = " " })
        };

        Assert.Equal("baltyk_cargo", AutomationProperties.GetName(item));
    }

    [Fact]
    public void A_list_entry_joins_the_texts_it_shows()
    {
        var item = new ListBoxItem
        {
            Content = new Border { Child = Row(new TextBlock { Text = "PKPIC" }, new TextBlock { Text = "303E-EP-426" }) }
        };

        Assert.Equal("PKPIC, 303E-EP-426", AutomationProperties.GetName(item));
    }

    [Fact]
    public void Text_made_of_runs_is_read_whole()
    {
        var block = new TextBlock();
        block.Inlines!.Add(new Run("EP07"));
        block.Inlines.Add(new LineBreak());
        block.Inlines.Add(new Span { Inlines = { new Run("544") } });

        Assert.Equal("EP07 544", ScreenReaderNames.TextOf(block));
    }

    [Fact]
    public void Nothing_to_read_gives_no_name()
    {
        Assert.Null(ScreenReaderNames.TextOf(null));
        Assert.Null(ScreenReaderNames.TextOf(new Image()));
        Assert.Null(ScreenReaderNames.TextOf("   "));
        Assert.Equal("Opis", ScreenReaderNames.TextOf(new ContentControl { Content = " Opis " }));
    }

    [Fact]
    public void A_field_is_named_by_the_hint_written_in_it()
    {
        var search = new TextBox { PlaceholderText = "Szukaj…" };
        var category = new ComboBox { PlaceholderText = "Kategoria" };

        Assert.Equal("Szukaj…", AutomationProperties.GetName(search));
        Assert.Equal("Kategoria", AutomationProperties.GetName(category));
    }

    [Fact]
    public void A_setting_row_names_its_inputs_by_its_label()
    {
        var combo = new ComboBox();
        var slider = new Slider();
        var browse = new Button { Content = "Wybierz plik…" };
        var row = new SettingRow { Content = new Border { Child = Row(combo, slider, browse) } };
        row.Label = "Wybór exe";

        Assert.Equal("Wybór exe", AutomationProperties.GetName(combo));
        Assert.Equal("Wybór exe", AutomationProperties.GetName(slider));
        Assert.Null(AutomationProperties.GetName(browse));
    }

    [Fact]
    public void A_setting_row_describes_its_inputs_with_its_explanation()
    {
        var check = new CheckBox { Content = "Tryb pełnoekranowy" };
        var slider = new Slider();
        var row = new SettingRow { Label = "Ekran", Content = Row(check, slider) };
        row.Description = "Gra zajmie cały ekran.";
        row.IdleNote = "Ten symulator nie zna tego ustawienia.";

        Assert.Equal("Gra zajmie cały ekran., Ten symulator nie zna tego ustawienia.", AutomationProperties.GetHelpText(check));
        Assert.Equal(AutomationProperties.GetHelpText(check), AutomationProperties.GetHelpText(slider));
        Assert.Null(AutomationProperties.GetName(check));

        row.Description = null;
        row.IdleNote = null;
        Assert.Null(AutomationProperties.GetHelpText(check));
    }

    [Fact]
    public void A_description_given_on_purpose_is_left_alone()
    {
        var box = new TextBox();
        AutomationProperties.SetHelpText(box, "Ścieżka do exe");
        _ = new SettingRow { Content = box, Description = "Plik symulatora" };

        Assert.Equal("Ścieżka do exe", AutomationProperties.GetHelpText(box));
    }

    [Fact]
    public void A_setting_row_without_a_label_leaves_its_input_unnamed()
    {
        var box = new TextBox();
        _ = new SettingRow { Content = box };

        Assert.Null(AutomationProperties.GetName(box));
    }
}

public class PickerNameTests
{
    [Fact]
    public void A_picker_is_read_as_its_label_and_its_value()
    {
        Assert.Equal("Godzina: 07:05", ScreenReaderNames.PickerText("Godzina", "07:05"));
        Assert.Equal("Data", ScreenReaderNames.PickerText("Data", null));
        Assert.Equal("07:05", ScreenReaderNames.PickerText(null, "07:05"));
        Assert.Null(ScreenReaderNames.PickerText("", null));
    }

    [Fact]
    public void A_date_is_read_in_words_with_the_year_only_when_shown()
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
        try
        {
            var date = new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.FromHours(1));

            Assert.Equal("27 października", ScreenReaderNames.DateText(date, withYear: false));
            Assert.Equal("wtorek, 27 października 2026", ScreenReaderNames.DateText(date, withYear: true));
            Assert.Null(ScreenReaderNames.DateText(null, withYear: false));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void A_time_is_read_as_hours_and_minutes()
    {
        Assert.Equal("07:05", ScreenReaderNames.TimeText(new TimeSpan(7, 5, 30)));
        Assert.Null(ScreenReaderNames.TimeText(null));
    }
}
