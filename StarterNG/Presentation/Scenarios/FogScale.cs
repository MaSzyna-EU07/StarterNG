using System;
using System.Globalization;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Presentation.Scenarios;

public static class FogScale
{
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

    public static string Format(int metres, CultureInfo culture) =>
        metres < 1000
            ? $"{metres} m"
            : string.Format(culture, "{0:0.#} km", metres / 1000.0);
}
