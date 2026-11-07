using System;
using System.Collections.Generic;
using Avalonia.Controls;
using StarterNG.Domain.Settings;

namespace StarterNG.Views;

/// <summary>
/// A slider that moves through a list of values rather than along a range: its
/// position is an index, so the values sit evenly on the track however far apart
/// they are - 30 and 144 frames alike. A value the list lacks joins it while
/// shown, so opening and saving the settings never moves it.
/// </summary>
internal sealed class SliderSteps
{
    private readonly Slider _slider;
    private readonly IReadOnlyList<double> _steps;
    private IReadOnlyList<double> _shown;

    public SliderSteps(Slider slider, IReadOnlyList<double> steps)
    {
        _slider = slider;
        _steps = steps;
        _shown = steps;
        slider.Minimum = 0;
        slider.TickFrequency = 1;
        slider.IsSnapToTickEnabled = true;
        slider.Maximum = steps.Count - 1;
    }

    public double Value
    {
        get => _shown[Math.Clamp((int)Math.Round(_slider.Value), 0, _shown.Count - 1)];
        set
        {
            _shown = Steps.With(_steps, value);
            _slider.Maximum = _shown.Count - 1;
            int at = Steps.IndexOf(_shown, value);
            _slider.Value = at;
        }
    }
}
