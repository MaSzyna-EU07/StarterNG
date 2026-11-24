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

    // above the longest step the simulator makes at once (its frame is held to a second) and a line's interval: a
    // stall of the simulator, a dragged window say, is time driven or stood, not a gap
    private const double MinimumGapSeconds = 2.0;
    private const int GapSteps = 20;

    public HaslerTape(IReadOnlyList<TapeSample> samples) : this(samples, GapAbove(samples))
    {
    }

    // the gap already found, by the reader: the steps are sorted once, not again here
    private HaslerTape(IReadOnlyList<TapeSample> samples, double gap)
    {
        Samples = samples;
        foreach (var s in samples)
            MaxSpeedKmh = Math.Max(MaxSpeedKmh, s.SpeedKmh);
        GapSeconds = gap;
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

    /// <summary>A step between two lines longer than this is a gap: <see cref="GapAbove"/> of this tape.</summary>
    public double GapSeconds { get; }

    public double StandingTime => Duration - RunningTime - GapTime;

    /// <summary>Mean speed while moving.</summary>
    public double AverageSpeedKmh => RunningTime > 0 ? DistanceM / RunningTime * 3.6 : 0;

    /// <summary>Mean speed over the whole recording, stops included - the commercial speed.</summary>
    public double AverageWithStopsKmh => Duration - GapTime > 0 ? DistanceM / (Duration - GapTime) * 3.6 : 0;

    /// <summary>
    /// A step between two lines longer than this is time not recorded, the player in another vehicle before
    /// coming back to this one: neither driving nor standing, and no distance. The lines come every 0.05 s of
    /// simulated time or so; it is twenty of their usual steps, two seconds at least.
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
        var reader = new Reader();
        reader.Feed(text);
        return reader.Tape(final: true);
    }

    /// <summary>
    /// A tape read as it is written: each piece of the file given once, in order, so a log the simulator keeps
    /// adding to - fast, at a higher time speed - is not read and split from its start every time it grows. A line
    /// not ended yet waits for the rest.
    /// </summary>
    public sealed class Reader
    {
        private readonly List<TapeSample> _samples = new();
        private readonly System.Text.StringBuilder _unended = new();
        private int _securityFromEnd = SecurityFromEndOf25;

        public void Feed(string text)
        {
            WentBack = false;
            int start = 0;
            for (int end = text.IndexOf('\n'); end >= 0; end = text.IndexOf('\n', start))
            {
                if (_unended.Length > 0)
                {
                    _unended.Append(text, start, end - start);
                    Keep(_unended.ToString(), _samples);
                    _unended.Clear();
                }
                else
                    Keep(text.Substring(start, end - start), _samples);
                start = end + 1;
            }
            _unended.Append(text, start, text.Length - start);
        }

        /// <summary>
        /// The tape of what was read. Final, the end of the file: a last line with no end is read too, though
        /// only for this tape; otherwise it may be cut short by a simulator still writing, and waits.
        /// </summary>
        public HaslerTape Tape(bool final)
        {
            var read = _samples;
            if (final && _unended.Length > 0)
            {
                read = new List<TapeSample>(_samples);
                Keep(_unended.ToString(), read);
            }
            // the distance, trapezoid by trapezoid, none over the gaps
            var samples = new List<TapeSample>(read.Count);
            double gap = GapAbove(read), distance = 0;
            for (int i = 0; i < read.Count; i++)
            {
                var sample = read[i];
                if (i > 0)
                {
                    var before = read[i - 1];
                    double step = sample.Time - before.Time;
                    if (step <= gap)
                        distance += (before.SpeedKmh + sample.SpeedKmh) / 2 / 3.6 * step;
                }
                samples.Add(sample with { DistanceM = distance });
            }
            return new HaslerTape(samples, gap);
        }

        private void Keep(string raw, List<TapeSample> samples)
        {
            var columns = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length == 0)
                return;
            if (!Number(columns[TimeColumn], out double time))
            {
                // the header; a line of figures may have the word too, in the free text of its command
                int heading = Array.IndexOf(columns, SecurityHeading);
                if (heading >= 0)
                    _securityFromEnd = columns.Length - heading;
                return;
            }
            if (columns.Length < MinimumColumns || !Number(columns[VelocityColumn], out double velocity))
                return; // a line cut short by a simulator still writing

            Number(columns[BrakePressColumn], out double brake);
            Number(columns[PipePressColumn], out double pipe);
            Number(columns[MotorCurrentColumn], out double current);
            bool security = _securityFromEnd <= columns.Length && columns[^_securityFromEnd] == "1";

            if (samples.Count > 0 && time < samples[^1].Time)
            {
                WentBack = true;
                return;
            }
            samples.Add(new TapeSample(time, Math.Abs(velocity), 0, brake, pipe, current, security));
        }

        /// <summary>In the last piece fed, a line whose time was before the last one's, left out: maybe the log begun anew.</summary>
        public bool WentBack { get; private set; }
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
