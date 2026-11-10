using System;
using System.Collections.Generic;
using System.Linq;

namespace Eu07.BlackBox;

/// <summary>How the user's train kept to a station of its timetable.</summary>
public enum StationOutcome
{
    /// <summary>Not reached yet in the recording.</summary>
    Ahead,
    /// <summary>Stopped where the timetable has a stop.</summary>
    Stopped,
    /// <summary>Passed where the timetable has no stop.</summary>
    Passed,
    /// <summary>Left a stop behind without stopping, or before the departure.</summary>
    Missed
}

/// <summary>How late the user's train was at a moment [min, above 0 late], after the station named, standing there or not.</summary>
public sealed record TrainDelay(string Train, double Minutes, string Station, bool Standing);

/// <summary>
/// A row of the timetable of the user's train, with what happened there. The times are
/// minutes of the day, as in the timetable; the delays are in minutes, above 0 late.
/// </summary>
public sealed record StationProgress(
    int Number,
    TimetableRow Row,
    StationOutcome Outcome,
    double? ArrivedAt,
    double? DepartedAt,
    double? ArrivalClock,
    double? DepartureClock,
    StationEvent? Event)
{
    public double? ArrivalDelay => Delay(ArrivalClock, Row.Arrival);

    /// <summary>Of the departure, or of the pass for a station passed through.</summary>
    public double? DepartureDelay => Delay(DepartureClock, Row.Departure);

    /// <summary>Above 0 the head stood short of where it was to stop, below 0 past it [m].</summary>
    public float? StopError => Outcome == StationOutcome.Stopped && Event is { } e ? e.StopError : null;

    public bool? OnPlatform => Outcome == StationOutcome.Stopped ? Event?.OnPlatform : null;

    /// <summary>How far the tail stood off the platform [m], when it stopped.</summary>
    public double? OffPlatform => Outcome == StationOutcome.Stopped ? Event?.OffPlatform : null;

    /// <summary>The difference of two times of the day in minutes, across midnight the short way.</summary>
    internal static double? Delay(double? actual, float planned)
    {
        if (actual is not { } a || planned < 0)
            return null;
        double d = a - planned;
        if (d > 720) d -= 1440;
        if (d < -720) d += 1440;
        return d;
    }

    /// <summary>
    /// The rows of the timetable the user's train had at t, each with what the train did
    /// there by the end of the recording read so far.
    /// </summary>
    public static IReadOnlyList<StationProgress> Of(Recording recording, double t) =>
        recording.TimetableAt(t) is { } timetable ? Of(recording, timetable) : Array.Empty<StationProgress>();

    /// <summary>The rows of a timetable of the recording, each with what the train did there.</summary>
    public static IReadOnlyList<StationProgress> Of(Recording recording, Timetable timetable)
    {
        // The records of this train: from its timetable on, until another one is written. A train given its next
        // timetable may come to the first station of it at once, and a recording made before the simulator wrote the
        // timetable first has that station up to a look (1 s) before it: it goes with the timetable after.
        int next = IndexOf(recording.Timetables, timetable) + 1;
        double until = next < recording.Timetables.Count ? recording.Timetables[next].Time - 1 : double.MaxValue;
        var events = recording.UserStations()
                              .Where(e => e.Time >= timetable.Time - 1 && e.Time < until)
                              .ToList();

        var rows = new List<StationProgress>(timetable.Rows.Count);
        for (int number = 1; number <= timetable.Rows.Count; number++)
        {
            var row = timetable.Rows[number - 1];
            var mine = events.Where(e => e.Row == number).ToList();
            var stopped = mine.FirstOrDefault(e => e.Kind == StationKind.Stopped);
            var passed = mine.FirstOrDefault(e => e.Kind == StationKind.Passed);
            var missed = mine.FirstOrDefault(e => e.Kind == StationKind.Missed);
            if (stopped is not null)
            {
                double? departed = DepartureAfter(recording, stopped.Time);
                rows.Add(new StationProgress(number, row, missed is not null ? StationOutcome.Missed : StationOutcome.Stopped,
                                             stopped.Time, departed, Minutes(recording, stopped.Time),
                                             departed is { } d ? Minutes(recording, d) : null, stopped));
            }
            else if (passed is not null)
                rows.Add(new StationProgress(number, row, StationOutcome.Passed, null, passed.Time, null,
                                             Minutes(recording, passed.Time), passed));
            else if (missed is not null)
                rows.Add(new StationProgress(number, row, StationOutcome.Missed, null, missed.Time, null,
                                             Minutes(recording, missed.Time), missed));
            else
                rows.Add(new StationProgress(number, row, StationOutcome.Ahead, null, null, null, null, null));
        }
        return rows;
    }

    /// <summary>When the user's train got moving again after a stop, null while it still stood.</summary>
    private static double? DepartureAfter(Recording recording, double stoppedAt)
    {
        var cab = recording.Cab;
        for (int i = Math.Max(0, recording.CabIndexAt(stoppedAt)); i < cab.Count; i++)
            if (cab[i].Time > stoppedAt && cab[i].AbsSpeedKmh > 2)
                return cab[i].Time;
        return null;
    }

    /// <summary>
    /// The delay of the user's train at t, from the last station of its timetable it had come to
    /// by then: of the departure or the pass once it left, of the arrival while it stands, growing
    /// while it stands past the departure, and growing on the way once it is past the time it was
    /// to be at the next station. Null before the first station or without a timetable.
    /// </summary>
    public static TrainDelay? DelayAt(Recording recording, double t)
    {
        if (recording.TimetableAt(t) is not { } timetable || t < timetable.Time - 1)
            return null;
        double clock = recording.ClockAt(t).TotalMinutes;
        TrainDelay? delay = null;
        var rows = Of(recording, timetable);
        int reached = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.DepartedAt is { } departed && departed <= t)
                delay = new TrainDelay(timetable.Train, row.DepartureDelay ?? row.ArrivalDelay ?? delay?.Minutes ?? 0, row.Row.Station, false);
            else if (row.ArrivedAt is { } arrived && arrived <= t)
            {
                double minutes = row.ArrivalDelay ?? delay?.Minutes ?? 0;
                if (Delay(clock, row.Row.Departure) is { } overdue)
                    minutes = Math.Max(minutes, overdue);
                delay = new TrainDelay(timetable.Train, minutes, row.Row.Station, true);
            }
            else
                continue;
            reached = i;
        }
        // On the way, past the time it was to be at the next station: at least that late, and more as it stays away.
        if (delay is { Standing: false } && reached + 1 < rows.Count)
        {
            var next = rows[reached + 1].Row;
            if (Delay(clock, next.Arrival >= 0 ? next.Arrival : next.Departure) is { } overdue && overdue > delay.Minutes)
                delay = delay with { Minutes = overdue };
        }
        return delay;
    }

    /// <summary>The delays the user's train had at the stations it had left or stood at by t, by the name shown.</summary>
    public static IReadOnlyDictionary<string, double> StationDelays(Recording recording, double t)
    {
        var delays = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var timetable in recording.Timetables)
        {
            if (timetable.Time > t)
                break;
            foreach (var row in Of(recording, timetable))
            {
                double? at = row.ArrivedAt ?? row.DepartedAt;
                double? delay = row.DepartedAt <= t ? row.DepartureDelay ?? row.ArrivalDelay : row.ArrivalDelay;
                if (at <= t && delay is { } d)
                    delays[StationNames.Display(row.Row.Station)] = d;
            }
        }
        return delays;
    }

    private static int IndexOf(IReadOnlyList<Timetable> timetables, Timetable timetable)
    {
        for (int i = 0; i < timetables.Count; i++)
            if (ReferenceEquals(timetables[i], timetable))
                return i;
        return -1;
    }

    private static double Minutes(Recording recording, double t) => recording.ClockAt(t).TotalMinutes;
}
