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
    // the command, which is free text, as far from the end as the header has it: the
    // simulator adds a new column at the end (the vehicle's speed, 2026-10). The header
    // says m/s and MPa, the values are km/h and bar.
    private const int TimeColumn = 0;
    private const int VelocityColumn = 1;
    private const int BrakePressColumn = 8;
    private const int PipePressColumn = 9;
    private const int MotorCurrentColumn = 10;
    private const string SecurityHeading = "Security";
    private const int SecurityFromEndOf25 = 6; // a log with no header to tell: the columns up to the water's temperature
    private const int MinimumColumns = 17;

    private const double MinimumGapSeconds = 1.0;
    private const int GapSteps = 20;

    public HaslerTape(IReadOnlyList<TapeSample> samples)
    {
        Samples = samples;
        foreach (var s in samples)
            MaxSpeedKmh = Math.Max(MaxSpeedKmh, s.SpeedKmh);
        double gap = GapAbove(samples);
        for (int i = 1; i < samples.Count; i++)
        {
            double step = samples[i].Time - samples[i - 1].Time;
            if (step > gap)
                GapTime += step;
            else if (samples[i].SpeedKmh > 0.5)
                RunningTime += step;
        }
    }

    public IReadOnlyList<TapeSample> Samples { get; }

    public bool IsEmpty => Samples.Count == 0;

    public double StartTime => IsEmpty ? 0 : Samples[0].Time;

    public double Duration => IsEmpty ? 0 : Samples[^1].Time - Samples[0].Time;

    public double DistanceM => IsEmpty ? 0 : Samples[^1].DistanceM;

    public double MaxSpeedKmh { get; }

    /// <summary>Seconds on the move; the rest of <see cref="Duration"/> was spent standing, or not recorded.</summary>
    public double RunningTime { get; }

    /// <summary>Seconds of <see cref="Duration"/> not recorded: the gaps, see <see cref="GapAbove"/>.</summary>
    public double GapTime { get; }

    public double StandingTime => Duration - RunningTime - GapTime;

    /// <summary>Mean speed while moving.</summary>
    public double AverageSpeedKmh => RunningTime > 0 ? DistanceM / RunningTime * 3.6 : 0;

    /// <summary>Mean speed over the whole recording, stops included - the commercial speed.</summary>
    public double AverageWithStopsKmh => Duration - GapTime > 0 ? DistanceM / (Duration - GapTime) * 3.6 : 0;

    /// <summary>
    /// A step between two lines longer than this is time not recorded, the player in another vehicle before
    /// coming back to this one: neither driving nor standing, and no distance. The lines come every 0.05 s of
    /// simulated time or so; it is twenty of their usual steps, a second at least.
    /// </summary>
    public static double GapAbove(IReadOnlyList<TapeSample> samples)
    {
        if (samples.Count < 2)
            return MinimumGapSeconds;
        var steps = new double[samples.Count - 1];
        for (int i = 1; i < samples.Count; i++)
            steps[i - 1] = samples[i].Time - samples[i - 1].Time;
        Array.Sort(steps);
        return Math.Max(MinimumGapSeconds, GapSteps * steps[steps.Length / 2]);
    }

    public static HaslerTape Parse(string text)
    {
        var samples = new List<TapeSample>();

        int securityFromEnd = SecurityFromEndOf25;
        foreach (var raw in text.Split('\n'))
        {
            var columns = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length == 0)
                continue;
            if (!Number(columns[TimeColumn], out double time))
            {
                // the header; a line of figures may have the word too, in the free text of its command
                int heading = Array.IndexOf(columns, SecurityHeading);
                if (heading >= 0)
                    securityFromEnd = columns.Length - heading;
                continue;
            }
            if (columns.Length < MinimumColumns || !Number(columns[VelocityColumn], out double velocity))
                continue; // a line cut short by a simulator still writing

            Number(columns[BrakePressColumn], out double brake);
            Number(columns[PipePressColumn], out double pipe);
            Number(columns[MotorCurrentColumn], out double current);
            bool security = securityFromEnd <= columns.Length && columns[^securityFromEnd] == "1";

            if (samples.Count > 0 && time < samples[^1].Time)
                continue;
            samples.Add(new TapeSample(time, Math.Abs(velocity), 0, brake, pipe, current, security));
        }

        // the distance, trapezoid by trapezoid, none over the gaps
        double gap = GapAbove(samples), distance = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            var (before, sample) = (samples[i - 1], samples[i]);
            double step = sample.Time - before.Time;
            if (step <= gap)
                distance += (before.SpeedKmh + sample.SpeedKmh) / 2 / 3.6 * step;
            samples[i] = sample with { DistanceM = distance };
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
