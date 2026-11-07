using System;
using System.Globalization;

namespace StarterNG.Domain.Tapes;

/// <summary>The figures a tape is printed and read with, which no drawing is needed to work out.</summary>
public static class TapeFigures
{
    private static readonly double[] SpeedRanges = { 90, 100, 120, 150, 180 };
    private static readonly double[] SpeedSteps = { 10, 20, 50, 100, 200 };
    private static readonly double[] MinuteSteps = { 1, 2, 5, 10, 15, 30, 60 };
    private static readonly double[] KmSteps = { 0.1, 0.2, 0.5, 1, 2, 5, 10, 20 };

    /// <summary>The scale is printed for the speedometer's range: 90, 100, 120, 150 or 180 km/h.</summary>
    public static double TopSpeed(double maxKmh)
    {
        foreach (double range in SpeedRanges)
            if (maxKmh <= range)
                return range;
        return Math.Ceiling(maxKmh / 20) * 20;
    }

    /// <summary>A round number of km/h between value lines, far enough apart for their figures to stay readable.</summary>
    public static int SpeedStep(double pxPerKmh) => (int)FirstThatFits(SpeedSteps, pxPerKmh, 8, 500);

    /// <summary>A round number of minutes between clock lines, about every 60 pixels or more.</summary>
    public static double MinuteStep(double pxPerMinute) => FirstThatFits(MinuteSteps, pxPerMinute, 60, 120);

    /// <summary>A round number of kilometres between distance marks, 50 pixels apart or more.</summary>
    public static double KmStep(double pxPerKm) => FirstThatFits(KmSteps, pxPerKm, 50, 50);

    private static double FirstThatFits(double[] steps, double pxPerUnit, double minPx, double otherwise)
    {
        foreach (double step in steps)
            if (step * pxPerUnit >= minPx)
                return step;
        return otherwise;
    }

    /// <summary>Hours only when there are any: 7:05, 1:02:03. Below zero counts as zero.</summary>
    public static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>A time of day as typed; anything that is not one is midnight.</summary>
    public static TimeSpan ParseClock(string? text) =>
        TimeSpan.TryParseExact(text?.Trim(), new[] { @"h\:mm", @"hh\:mm", @"h\:mm\:ss" }, CultureInfo.InvariantCulture,
                               out var clock) && clock < TimeSpan.FromDays(1)
            ? clock
            : TimeSpan.Zero;
}
