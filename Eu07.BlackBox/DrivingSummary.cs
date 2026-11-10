using System;
using System.Collections.Generic;
using System.Linq;

namespace Eu07.BlackBox;

/// <summary>How a sample of the user's train stands against what the AI would have done.</summary>
public enum SpeedJudgement
{
    /// <summary>Nothing to set it against: the consist not started, or the user out of the cab.</summary>
    NoData,
    /// <summary>The AI drove.</summary>
    Autopilot,
    /// <summary>At most the speed the AI would hold.</summary>
    Within,
    /// <summary>Faster, by no more than the tolerance.</summary>
    Slightly,
    /// <summary>Faster by more than the tolerance.</summary>
    Over
}

/// <summary>What the user's driving came to, set against the AI's.</summary>
public sealed record DrivingSummary(
    double Duration,
    double DrivenSeconds,
    double AutopilotSeconds,
    double DistanceKm,
    float MaxSpeedKmh,
    double OverAiSeconds,
    double WellOverAiSeconds,
    float MaxOverAiKmh,
    double MaxOverAiAt,
    int SignalsPassed,
    IReadOnlyList<SignalPassing> PassedAtDanger,
    int Commands,
    int AutopilotSwitches,
    float MaxAcceleration,
    float MaxDeceleration,
    float MaxLateralAcceleration,
    int Stops,
    int StopsMissed,
    double? MaxDelayMinutes,
    int HornPoints,
    int HornsMissed,
    int SecurityBrakings)
{
    /// <summary>The largest delay the user's train left a station or passed it with [min], null without any.</summary>
    public double? MaxDepartureDelayMinutes { get; init; }

    /// <summary>How long the train moved with its light signals known to be right or wrong [s].</summary>
    public double LightsJudgedSeconds { get; init; }

    /// <summary>How long of that its head or its tail showed other signals than it was to [s].</summary>
    public double WrongLightsSeconds { get; init; }

    /// <summary>Each stretch the train moved with the wrong signals, as they were.</summary>
    public IReadOnlyList<LightsFault> LightsFaults { get; init; } = Array.Empty<LightsFault>();

    /// <summary>The stops where the train did not fit along the platform, and how far its tail stood off it [m].</summary>
    public IReadOnlyList<(string Station, double Metres)> OffPlatform { get; init; } = Array.Empty<(string, double)>();

    /// <summary>[km/h] over the speed of the AI before it counts as well over.</summary>
    public const float DefaultTolerance = 5f;

    /// <summary>
    /// The speeds are whole km/h for the AI and the needle of the user's train shakes:
    /// half a km/h over is still within.
    /// </summary>
    private const float Rounding = 0.5f;

    public static SpeedJudgement Judge(CabSample sample, float tolerance = DefaultTolerance)
    {
        if (sample.Driving == Driver.Ai)
            return SpeedJudgement.Autopilot;
        if (!sample.AiValid || sample.Driving != Driver.Human)
            return SpeedJudgement.NoData;
        float over = sample.AbsSpeedKmh - sample.Ai.TargetKmh;
        return over <= Rounding ? SpeedJudgement.Within : over <= tolerance ? SpeedJudgement.Slightly : SpeedJudgement.Over;
    }

    internal static DrivingSummary Of(Recording recording, float tolerance)
    {
        var cab = recording.Cab;
        double driven = 0, autopilot = 0, distance = 0, over = 0, wellOver = 0, maxOverAt = 0;
        float maxSpeed = 0, maxOver = 0, maxAcceleration = 0, maxDeceleration = 0, maxLateral = 0;
        // The accelerations over half a second: a sample alone jumps with each jolt of the couplers.
        var window = new Queue<(float Along, float Across)>();
        float sumAlong = 0, sumAcross = 0;
        double lightsJudged = 0, lightsWrong = 0;
        var faults = new List<LightsFault>();
        // A step longer than this is a gap in the samples, not driving.
        double gap = (recording.Info is { CabInterval: > 0 } info ? info.CabInterval : 0.1) * 5;
        for (int i = 1; i < cab.Count; i++)
        {
            var before = cab[i - 1];
            var sample = cab[i];
            double step = sample.Time - before.Time;
            if (step <= 0 || step > gap || sample.Vehicle != before.Vehicle)
            {
                window.Clear();
                sumAlong = sumAcross = 0;
                continue;
            }

            distance += (before.AbsSpeedKmh + sample.AbsSpeedKmh) / 2 / 3.6 * step / 1000;
            window.Enqueue((sample.Acceleration, sample.LateralAcceleration));
            sumAlong += sample.Acceleration;
            sumAcross += sample.LateralAcceleration;
            if (window.Count > 5)
            {
                var (along, across) = window.Dequeue();
                sumAlong -= along;
                sumAcross -= across;
            }
            if (window.Count == 5 && sample.AbsSpeedKmh > 1)
            {
                maxAcceleration = Math.Max(maxAcceleration, sumAlong / 5);
                maxDeceleration = Math.Max(maxDeceleration, -sumAlong / 5);
                maxLateral = Math.Max(maxLateral, Math.Abs(sumAcross / 5));
            }
            maxSpeed = Math.Max(maxSpeed, sample.AbsSpeedKmh);
            if (sample.Driving == Driver.Ai)
                autopilot += step;
            else if (sample.Driving == Driver.Human && sample.AbsSpeedKmh > Rounding)
                driven += step;

            // the light signals at both ends, while the train moves
            if (sample.AbsSpeedKmh > 1 && HeadCodes.Right(sample) is { } right)
            {
                lightsJudged += step;
                if (!right)
                {
                    lightsWrong += step;
                    var fault = new LightsFault(before.Time, sample.Time, HeadCodes.Of(sample.LightsHead), HeadCodes.RequiredHead(sample)!.Value,
                                                HeadCodes.Of(sample.LightsTail), HeadCodes.RequiredTail(sample)!.Value);
                    if (faults.Count > 0 && faults[^1].SameAs(fault) && fault.Start - faults[^1].End < 2)
                        faults[^1] = faults[^1] with { End = sample.Time };
                    else
                        faults.Add(fault);
                }
            }

            var judgement = Judge(sample, tolerance);
            if (judgement is SpeedJudgement.Slightly or SpeedJudgement.Over)
                over += step;
            if (judgement == SpeedJudgement.Over)
                wellOver += step;
            if (judgement is SpeedJudgement.Slightly or SpeedJudgement.Over)
            {
                float by = sample.AbsSpeedKmh - sample.Ai.TargetKmh;
                if (by > maxOver)
                {
                    maxOver = by;
                    maxOverAt = sample.Time;
                }
            }
        }

        var passed = recording.UserPassings().ToList();
        int switches = recording.DriverChanges.Count(change =>
            recording.CabAt(change.Time + 0.5) is { } s && s.Vehicle == change.Vehicle);
        // Every timetable the user's train had, as one going on from another at a terminus.
        var stations = recording.Timetables.SelectMany(timetable => StationProgress.Of(recording, timetable)).ToList();
        // the largest delay at any station, coming to it or leaving it
        var delays = stations.SelectMany(s => new[] { s.ArrivalDelay, s.DepartureDelay }).OfType<double>().ToList();
        var departures = stations.Select(s => s.DepartureDelay).OfType<double>().ToList();
        var offPlatform = stations.Where(s => s.OnPlatform == false)
                                  .Select(s => (StationNames.Display(s.Row.Station), s.OffPlatform ?? 0))
                                  .ToList();
        var horns = recording.UserHornChecks().ToList();
        return new DrivingSummary(recording.Duration, driven, autopilot, distance, maxSpeed, over, wellOver, maxOver,
                                  maxOverAt, passed.Count, passed.Where(p => p.AtDanger).ToList(),
                                  recording.UserCommands().Count(), switches, maxAcceleration, maxDeceleration, maxLateral,
                                  stations.Count(s => s.Outcome == StationOutcome.Stopped),
                                  stations.Count(s => s.Outcome == StationOutcome.Missed),
                                  delays.Count > 0 ? delays.Max() : null,
                                  horns.Count, horns.Count(h => !h.Sounded), recording.SecurityBrakings().Count)
        {
            MaxDepartureDelayMinutes = departures.Count > 0 ? departures.Max() : null,
            LightsJudgedSeconds = lightsJudged,
            WrongLightsSeconds = lightsWrong,
            LightsFaults = faults,
            OffPlatform = offPlatform
        };
    }
}

/// <summary>A stretch the user's train moved with other light signals than it was to show, at its head and its tail.</summary>
public sealed record LightsFault(double Start, double End, HeadCode Head, HeadCode HeadRequired, HeadCode Tail, HeadCode TailRequired)
{
    public bool HeadWrong => Head != HeadRequired;
    public bool TailWrong => Tail != TailRequired;

    public bool SameAs(LightsFault other) =>
        Head == other.Head && HeadRequired == other.HeadRequired && Tail == other.Tail && TailRequired == other.TailRequired;
}
