using System;
using StarterNG.Application.Abstractions;

namespace StarterNG.Domain.Sceneries;

public sealed class SceneryWeather
{
    public const int FogMin = 10;
    public const int FogMax = 10000;

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

    public int Day { get; set; }

    public double Temperature { get; set; } = 15;

    public int FogStart { get; set; } = 10;

    public int FogEnd { get; set; } = 2000;

    public double Overcast { get; set; }

    public bool OvercastRandom { get; set; }

    public bool IsAuthored { get; set; }

    public bool Dirty { get; set; }

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

    public bool Changed =>
        Day != _authoredDay ||
        ScenarioTimeOverride != _authoredTimeOverride ||
        Math.Abs(Temperature - _authoredTemperature) > 0.001 ||
        FogEnd != _authoredFogEnd ||
        OvercastRandom != _authoredOvercastRandom ||
        (!OvercastRandom && Math.Abs(Overcast - _authoredOvercast) > 0.001);

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

    public void SetFogRange(int start, int end, IRandomSource random)
    {
        FogStart = Math.Clamp(start, FogMin, FogMax);
        end = Math.Clamp(end, FogStart, FogMax);
        FogEnd = end > FogStart ? random.Next(FogStart, end + 1) : end;
    }

    public double EffectiveOvercast(IRandomSource random) =>
        OvercastRandom ? OvercastPresets[random.Next(0, OvercastPresets.Length)] : Overcast;
}
