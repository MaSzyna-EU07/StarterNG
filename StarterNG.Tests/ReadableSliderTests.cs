using System.Globalization;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using StarterNG.Controls;

namespace StarterNG.Tests;

public class ReadableSliderTests
{
    [Fact]
    public void The_track_under_the_mouse_says_the_name_and_the_value()
    {
        var fog = new ReadableSlider { ValueText = "2,4 km" };
        Assert.Equal("2,4 km", fog.TrackText);

        AutomationProperties.SetName(fog, "Widoczność ");
        Assert.Equal("Widoczność: 2,4 km", fog.TrackText);
    }

    [Fact]
    public void A_slider_is_read_as_the_value_shown_beside_it()
    {
        var fog = new ReadableSlider { Minimum = 0, Maximum = 1000, Value = 523, ValueText = " 2,4 km " };

        Assert.Equal("2,4 km", fog.SpokenValue);
    }

    [Fact]
    public void Without_a_shown_value_the_number_itself_is_read()
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
        try
        {
            Assert.Equal("-5", ReadableSlider.SpokenText(null, -5));
            Assert.Equal("1,25", ReadableSlider.SpokenText(" ", 1.25));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void A_setting_row_gives_its_value_to_the_slider_in_it()
    {
        var slider = new ReadableSlider { Minimum = 0, Maximum = 100, Value = 40 };
        var row = new SettingRow { Content = new Border { Child = slider } };

        row.ValueText = "40%";
        Assert.Equal("40%", slider.SpokenValue);

        row.ValueText = null;
        Assert.Equal("40", slider.SpokenValue);
    }

    [Fact]
    public void A_screen_reader_is_given_the_text_and_may_set_a_number()
    {
        var slider = new ReadableSlider { Minimum = 0, Maximum = 1000, Value = 523, ValueText = "2 km" };
        var peer = ControlAutomationPeer.CreatePeerForElement(slider);
        var value = Assert.IsAssignableFrom<IValueProvider>(peer);

        Assert.Equal(AutomationControlType.Slider, peer.GetAutomationControlType());
        Assert.Equal("Slider", peer.GetClassName());
        Assert.Equal("2 km", value.Value);
        Assert.False(value.IsReadOnly);

        value.SetValue("300");
        Assert.Equal(300, slider.Value);
        Assert.Throws<ArgumentException>(() => value.SetValue("dużo"));
    }
}
