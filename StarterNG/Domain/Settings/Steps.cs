using System;
using System.Collections.Generic;

namespace StarterNG.Domain.Settings;

/// <summary>
/// Values a setting is chosen from, for a number the simulator takes any value
/// of but only some of which are worth offering.
/// </summary>
public static class Steps
{
    /// <summary>fpslimit: none, then from a laptop on battery up to the fastest monitors.</summary>
    public static readonly double[] FrameRateLimits =
        { 0, 20, 25, 30, 40, 45, 50, 60, 72, 75, 90, 100, 120, 144, 165, 180, 200, 240, 360 };

    /// <summary>
    /// gfx.reflections.framerate, in updates a second: from the simulator's own
    /// once in five minutes up to every frame.
    /// </summary>
    public static readonly double[] ReflectionUpdates =
        { 1 / 300.0, 1 / 60.0, 1 / 30.0, 0.1, 0.2, 0.5, 1, 2, 5, 10, 30, 60 };

    /// <summary>gfx.drawrange.factor.max: how far the regulator may stretch the draw range.</summary>
    public static readonly double[] DrawRangeFactors = { 1, 1.5, 2, 2.5, 3 };

    /// <summary>anisotropicfiltering: 16 is the most any card offers.</summary>
    public static readonly double[] Anisotropy = { 1, 2, 4, 8, 16 };

    /// <summary>
    /// multisampling, as the power of two of the samples: none to x8. The simulator
    /// takes 4 (x16) too, which only some cards can do - on one that cannot, the
    /// frame buffer fails to build - so it stays in the file but off the slider.
    /// </summary>
    public static readonly double[] MultisamplingLevels = { 0, 1, 2, 3 };

    /// <summary>
    /// <paramref name="steps"/> with <paramref name="value"/> among them: one
    /// eu07.ini has that is none of the steps stays what it is.
    /// </summary>
    public static IReadOnlyList<double> With(IReadOnlyList<double> steps, double value)
    {
        if (IndexOf(steps, value) >= 0 || !double.IsFinite(value))
            return steps;

        var list = new List<double>(steps);
        int at = list.FindIndex(step => step > value);
        list.Insert(at < 0 ? list.Count : at, value);
        return list;
    }

    /// <summary>The step that is <paramref name="value"/>, give or take its rounding in the file.</summary>
    public static int IndexOf(IReadOnlyList<double> steps, double value)
    {
        for (int i = 0; i < steps.Count; i++)
            if (Math.Abs(steps[i] - value) <= Math.Abs(steps[i]) * 2e-4)
                return i;
        return -1;
    }
}
