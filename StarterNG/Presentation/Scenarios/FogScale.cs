using System;
using System.Globalization;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Presentation.Scenarios;

/// <summary>
/// Converts between the weather tab's fog slider and a distance in metres.
/// </summary>
/// <remarks>
/// Visibility runs from 10 m to 10 km, a range no linear slider handles well:
/// the interesting part - thick fog, where a few metres matter - would be a
/// couple of pixels wide. The slider is therefore logarithmic over its 0-1000
/// travel, and the result snaps to a step that grows with the distance, so the
/// read-out lands on round numbers a driver would recognise.
/// </remarks>
public static class FogScale
{
    /// <summary>Travel of the slider the scale is spread over.</summary>
    public const double SliderRange = 1000.0;

    public static int ToMetres(double sliderPosition)
    {
        double metres = SceneryWeather.FogMin *
                        Math.Pow((double)SceneryWeather.FogMax / SceneryWeather.FogMin,
                                 sliderPosition / SliderRange);

        int step = metres < 100 ? 5 : metres < 1000 ? 10 : 100;
        return Math.Clamp((int)(Math.Round(metres / step) * step),
                          SceneryWeather.FogMin, SceneryWeather.FogMax);
    }

    public static double ToSlider(int metres)
    {
        double clamped = Math.Clamp(metres, SceneryWeather.FogMin, SceneryWeather.FogMax);
        return SliderRange * Math.Log(clamped / SceneryWeather.FogMin) /
               Math.Log((double)SceneryWeather.FogMax / SceneryWeather.FogMin);
    }

    /// <summary>Metres below a kilometre, kilometres above it.</summary>
    public static string Format(int metres, CultureInfo culture) =>
        metres < 1000
            ? $"{metres} m"
            : string.Format(culture, "{0:0.#} km", metres / 1000.0);
}
