using System;
using StarterNG.Application.Abstractions;

namespace StarterNG.Domain.Sceneries;

/// <summary>
/// The weather and time-of-day a scenery starts with: what the .scn file
/// declared, what the user has changed it to, and whether that differs.
/// </summary>
/// <remarks>
/// Split out of <c>Scenery</c> so the "is this still the authored weather?"
/// rule lives next to the values it compares, instead of being spread over the
/// aggregate and the weather tab. The aggregate owns one of these; the parser
/// fills it and calls <see cref="AcceptAsAuthored"/> once.
/// </remarks>
public sealed class SceneryWeather
{
    public const int FogMin = 10;
    public const int FogMax = 10000;

    /// <summary>Overcast values the "random" option picks between.</summary>
    private static readonly double[] OvercastPresets = { -1.5, 0, 0.3, 0.7, 1, 1.1, 1.5 };

    private string _authoredTime = "10:30";
    private string _authoredTimeOverride = "10:30";
    private int _authoredDay;
    private double _authoredTemperature = 15;
    private int _authoredFogEnd = 2000;
    private double _authoredOvercast;
    private bool _authoredOvercastRandom;

    public string Time { get; set; } = "10:30";

    public string ScenarioTimeOverride { get; set; } = "10:30";

    /// <summary>Day of the year driving the sun position (movelight).</summary>
    public int Day { get; set; }

    public double Temperature { get; set; } = 15;

    public int FogStart { get; set; } = 10;

    public int FogEnd { get; set; } = 2000;

    public double Overcast { get; set; }

    /// <summary>When set, each export draws a fresh overcast from the presets.</summary>
    public bool OvercastRandom { get; set; }

    /// <summary>True when the .scn actually declared weather, rather than us defaulting it.</summary>
    public bool IsAuthored { get; set; }

    /// <summary>Set once the user has touched the weather, so export rewrites the config block.</summary>
    public bool Dirty { get; set; }

    /// <summary>Takes the current values as the baseline "what the file said".</summary>
    public void AcceptAsAuthored()
    {
        _authoredTime = Time;
        _authoredTimeOverride = ScenarioTimeOverride;
        _authoredDay = Day;
        _authoredTemperature = Temperature;
        _authoredFogEnd = FogEnd;
        _authoredOvercast = Overcast;
        _authoredOvercastRandom = OvercastRandom;
    }

    /// <summary>True when the user's weather differs from what the .scn declared.</summary>
    public bool Changed =>
        Day != _authoredDay ||
        ScenarioTimeOverride != _authoredTimeOverride ||
        Math.Abs(Temperature - _authoredTemperature) > 0.001 ||
        FogEnd != _authoredFogEnd ||
        OvercastRandom != _authoredOvercastRandom ||
        (!OvercastRandom && Math.Abs(Overcast - _authoredOvercast) > 0.001);

    /// <summary>
    /// Puts back the authored weather. Stays <see cref="Dirty"/>: the export must
    /// still rewrite the config block, because the file on disk may already carry
    /// an edit from an earlier run.
    /// </summary>
    public void Restore()
    {
        Time = _authoredTime;
        ScenarioTimeOverride = _authoredTimeOverride;
        Day = _authoredDay;
        Temperature = _authoredTemperature;
        FogEnd = _authoredFogEnd;
        Overcast = _authoredOvercast;
        OvercastRandom = _authoredOvercastRandom;
        Dirty = true;
    }

    /// <summary>Clamps a parsed fog range and picks the distance to run with.</summary>
    public void SetFogRange(int start, int end, IRandomSource random)
    {
        FogStart = Math.Clamp(start, FogMin, FogMax);
        end = Math.Clamp(end, FogStart, FogMax);
        FogEnd = end > FogStart ? random.Next(FogStart, end + 1) : end;
    }

    /// <summary>The overcast to export, drawing a preset when randomisation is on.</summary>
    public double EffectiveOvercast(IRandomSource random) =>
        OvercastRandom ? OvercastPresets[random.Next(0, OvercastPresets.Length)] : Overcast;
}
