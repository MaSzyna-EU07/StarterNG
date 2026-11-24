using StarterNG.Domain.Settings;
using StarterNG.Views;

namespace StarterNG.Tests;

[Collection(AvaloniaControls.Name)]
public class SliderStepsTests
{
    [Fact]
    public void The_old_starters_steps_are_offered_and_a_value_outside_them_stays()
    {
        var slider = new Avalonia.Controls.Slider();
        var steps = new SliderSteps(slider, SliderRanges.ReflectionUpdates) { Value = 1 / 300.0 };

        // The simulator's own once in five minutes, shown before the old starter's 1 to 60.
        Assert.Equal(1 / 300.0, steps.Value);
        Assert.Equal(SliderRanges.ReflectionUpdates.Length, slider.Maximum);

        slider.Value = slider.Maximum;
        Assert.Equal(60, steps.Value);
    }

    [Fact]
    public void A_frame_limit_above_the_list_stays_as_the_file_has_it()
    {
        var slider = new Avalonia.Controls.Slider();
        var steps = new SliderSteps(slider, SliderRanges.FrameRateLimits) { Value = 300 };

        Assert.Equal(300, steps.Value);
        Assert.Equal(SliderRanges.FrameRateLimits.Length, slider.Maximum);
    }

    [Fact]
    public void Clamping_puts_a_value_past_either_end_at_that_end()
    {
        var slider = new Avalonia.Controls.Slider();
        var steps = new SliderSteps(slider, SliderRanges.FrameRateLimits) { Clamps = true, Value = 300 };

        Assert.Equal(240, steps.Value);
        Assert.Equal(SliderRanges.FrameRateLimits.Length - 1, slider.Maximum);
    }

    [Fact]
    public void Clamping_leaves_a_value_between_two_steps_where_it_is()
    {
        var slider = new Avalonia.Controls.Slider();
        var steps = new SliderSteps(slider, SliderRanges.CabShadowRanges) { Clamps = true, Value = 15 };

        Assert.Equal(15, steps.Value);

        steps.Value = -5;
        Assert.Equal(0, steps.Value);
    }
}
