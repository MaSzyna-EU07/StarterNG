using System;
using System.Collections.Generic;
using System.Globalization;

namespace StarterNG.Domain.Tapes;

/// <summary>One line of the simulator's physics log, in the units a speed recorder shows.</summary>
public readonly record struct TapeSample(
    double Time,
    double SpeedKmh,
    double DistanceM,
    double BrakePressBar,
    double PipePressBar,
    double MotorCurrentA,
    bool SecurityAlarm)
{
    /// <summary>Above this cylinder pressure the sample counts as braking.</summary>
    public const double BrakeMarkPressBar = 0.4;

    public bool Braking => BrakePressBar > BrakeMarkPressBar;

    public bool Current => MotorCurrentA != 0.0;
}

/// <summary>
/// A trip recorded by the simulator with "physicslog yes": physicslog/&lt;vehicle&gt;.dat,
/// a line every 0.05 s of simulated time, read as a Hasler speed recorder tape.
/// </summary>
public sealed class HaslerTape
{
    // Columns counted from the start; the security flag is counted from the end, past
    // the command, which is free text. The header says m/s and MPa, the values are km/h and bar.
    private const int TimeColumn = 0;
    private const int VelocityColumn = 1;
    private const int BrakePressColumn = 8;
    private const int PipePressColumn = 9;
    private const int MotorCurrentColumn = 10;
    private const int SecurityFromEnd = 6;
    private const int MinimumColumns = 17;

    public HaslerTape(IReadOnlyList<TapeSample> samples)
    {
        Samples = samples;
        foreach (var s in samples)
            MaxSpeedKmh = Math.Max(MaxSpeedKmh, s.SpeedKmh);
        for (int i = 1; i < samples.Count; i++)
            if (samples[i].SpeedKmh > 0.5)
                RunningTime += samples[i].Time - samples[i - 1].Time;
    }

    public IReadOnlyList<TapeSample> Samples { get; }

    public bool IsEmpty => Samples.Count == 0;

    public double StartTime => IsEmpty ? 0 : Samples[0].Time;

    public double Duration => IsEmpty ? 0 : Samples[^1].Time - Samples[0].Time;

    public double DistanceM => IsEmpty ? 0 : Samples[^1].DistanceM;

    public double MaxSpeedKmh { get; }

    /// <summary>Seconds on the move; the rest of <see cref="Duration"/> was spent standing.</summary>
    public double RunningTime { get; }

    public double StandingTime => Duration - RunningTime;

    /// <summary>Mean speed while moving.</summary>
    public double AverageSpeedKmh => RunningTime > 0 ? DistanceM / RunningTime * 3.6 : 0;

    /// <summary>Mean speed over the whole recording, stops included - the commercial speed.</summary>
    public double AverageWithStopsKmh => Duration > 0 ? DistanceM / Duration * 3.6 : 0;

    public static HaslerTape Parse(string text)
    {
        var samples = new List<TapeSample>();
        double distance = 0;
        TapeSample? previous = null;

        foreach (var raw in text.Split('\n'))
        {
            var columns = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < MinimumColumns
                || !Number(columns[TimeColumn], out double time)
                || !Number(columns[VelocityColumn], out double velocity))
                continue; // the header, or a line cut short by a simulator still writing

            Number(columns[BrakePressColumn], out double brake);
            Number(columns[PipePressColumn], out double pipe);
            Number(columns[MotorCurrentColumn], out double current);
            bool security = columns[^SecurityFromEnd] == "1";

            double speed = Math.Abs(velocity);
            if (previous is { } p)
            {
                if (time < p.Time)
                    continue;
                distance += (p.SpeedKmh + speed) / 2 / 3.6 * (time - p.Time);
            }

            var sample = new TapeSample(time, speed, distance, brake, pipe, current, security);
            samples.Add(sample);
            previous = sample;
        }

        return new HaslerTape(samples);
    }

    /// <summary>Index of the last sample at or before the given time or distance.</summary>
    public int IndexAt(double position, bool byDistance)
    {
        int lo = 0, hi = Samples.Count - 1;
        if (hi < 0)
            return -1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            double value = byDistance ? Samples[mid].DistanceM : Samples[mid].Time;
            if (value <= position)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo;
    }

    private static bool Number(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
