using Eu07.BlackBox;

namespace StarterNG.Tests.BlackBox;

/// <summary>
/// A journey of two timetables one after another: the stations, the horn points, the train
/// protection, the camera and the couplings, as the simulator writes them, and what the
/// recording tells of them.
/// </summary>
public class BlackBoxJourneyTests
{
    private const uint Head = 1, Car = 2, Tail = 3, Stranger = 9;
    private const byte Coupled = 0x83;

    /// <summary>
    /// The user in vehicle 1 of a unit 1-2-3, the clock at 10:34 (634 min). ROJ1 stands at A, stops at B
    /// at t = 60 until 70, passes C at 100 without stopping; ROJ2 from t = 110 stops at D at 115.
    /// </summary>
    private static Recording Journey()
    {
        var f = new BlackBoxFile().Info()
            .StopPoint(0, "Stacja_B#1", 1300, 10, 150)
            .StopPoint(1, "Stacja_B#2", 1310, 10, 150)
            .HornPoint(0, "w6_1", 1200)
            .User(0, Head, true, -1)
            .Coupling(0, Head, 1, Car, 0, Coupled, 0, 0)
            .Coupling(0, Car, 1, Tail, 0, Coupled, 0, 0)
            .Camera(0, 0, Head, 1150)
            .Timetable(0, Head, "ROJ1", "A", "C", ("A", -1, 634), ("Stacja_B", 635, 635.5f), ("C", 637.5f, 638))
            .Station(1, Head, 4, 1, "A", 1150);
        for (double t = 0; t <= 130; t += 0.5)
        {
            float speed = t is >= 55 and < 70 || t >= 112 && t < 120 ? 0 : 40;
            uint devices = 0x0007 | (t is >= 22 and < 24 ? 0x0200u : 0);
            byte security = (byte)(t is >= 30 and < 32 ? 0x11 : 0x01);
            f.Cab(t, Head, 1150 + t * 10, speed, devices: devices, security: security, lateral: t is >= 45 and < 48 ? 1.2f : 0.1f);
        }
        f.HornRequest(20, Head, 1200)
            .HornRequest(40, Head, 1550)
            .Camera(50, 2, 0, 1650)
            .Station(60, Head, 1, 2, "Stacja_B#2", 1300)
            .Station(60, Stranger, 1, 2, "Stacja_B#1", 9000)
            .Coupling(80, Car, 1, 0, 0, 0, Coupled, Tail)
            .Station(100, Head, 3, 3, "C", 2150)
            .Timetable(110, Head, "ROJ2", "C", "D", ("C", -1, 638), ("D", 640, -1))
            .Station(115, Head, 1, 2, "D", 2300);
        return BlackBoxReader.Read(f.End(130).Stream());
    }

    [Fact]
    public void The_new_records_are_read_field_by_field()
    {
        var r = Journey();

        Assert.Equal(0, r.Unknown);
        Assert.Equal(0, r.Malformed);
        Assert.Equal(2, r.StopPoints.Count);
        Assert.Equal("Stacja_B#2", r.StopPoints[1].Station);
        Assert.Equal(10f, r.StopPoints[1].StopAt);
        Assert.Equal(150f, r.StopPoints[1].Platform);
        Assert.Equal("w6_1", Assert.Single(r.HornPoints).Name);
        Assert.Equal(2, r.Timetables.Count);
        Assert.Equal("ROJ2", r.Timetables[1].Train);
        Assert.Equal(640f, r.Timetables[1].Row(2)!.Arrival);
        Assert.False(r.Timetables[1].Row(2)!.Maintenance);
        Assert.Equal(5, r.Stations.Count);
        Assert.Equal(StationKind.Missed, r.Stations.Single(s => s.Row == 3 && s.Vehicle == Head).Kind);
        Assert.Equal(2, r.HornRequests.Count);
        Assert.Equal(2, r.Cameras.Count);
        Assert.Equal(CameraMode.Free, r.Cameras[1].Mode);
        Assert.Equal(-0.1f, r.Cameras[1].Pitch);
        Assert.Equal(3, r.Couplings.Count);
        Assert.True(r.Couplings[2].Uncoupled);
        Assert.Equal(-1, Assert.Single(r.UserVehicles).CabEnd);

        var cab = r.Cab[0];
        Assert.Equal(1, cab.CabEnd);
        Assert.Equal(SecurityFlags.Vigilance, cab.Security);
        Assert.Equal(0.1f, cab.LateralAcceleration);
        Assert.Equal(Lights.HeadlightLeft | Lights.HeadlightUpper | Lights.HeadlightRight, cab.LightsHead);
    }

    [Fact]
    public void A_cab_written_before_the_protection_and_the_lateral_acceleration_is_still_read()
    {
        var r = BlackBoxReader.Read(new BlackBoxFile().Info().Cab(0, Head, 1150, 40, extended: false).User(0, Head, true).End(1).Stream());

        var cab = Assert.Single(r.Cab);
        Assert.Equal(0, cab.CabEnd);
        Assert.Equal(SecurityFlags.None, cab.Security);
        Assert.Equal(0f, cab.LateralAcceleration);
        Assert.Equal(0, Assert.Single(r.UserVehicles).CabEnd);
        Assert.Equal(0, r.Malformed);
    }

    [Fact]
    public void The_couplings_tell_the_train_at_any_moment()
    {
        var r = Journey();

        Assert.Equal(new HashSet<int> { 1, 2, 3 }, r.ConsistAt((int)Head, 79));
        Assert.Equal(new HashSet<int> { 1, 2 }, r.ConsistAt((int)Head, 81));
        Assert.Equal(new HashSet<int> { 3 }, r.ConsistAt((int)Tail, 81));
        // What the trains were made of at the start is no coupling done.
        var change = Assert.Single(r.CouplingEvents());
        Assert.Equal(80, change.Time);
        Assert.Single(r.UserCouplings());
    }

    [Fact]
    public void The_stations_of_someone_else_s_train_are_not_the_user_s()
    {
        var r = Journey();

        Assert.DoesNotContain(r.UserStations(), s => s.Vehicle == Stranger);
        Assert.Equal(4, r.UserStations().Count());
    }

    [Fact]
    public void The_timetable_shown_is_the_one_the_train_had_then()
    {
        var r = Journey();

        Assert.Equal("ROJ1", r.TimetableAt(100)!.Train);
        Assert.Equal("ROJ2", r.TimetableAt(110)!.Train);

        var first = StationProgress.Of(r, 50);
        Assert.Equal(3, first.Count);
        Assert.Equal(StationOutcome.Stopped, first[1].Outcome);
        Assert.Equal(60, first[1].ArrivedAt);
        Assert.Equal(70, first[1].DepartedAt);
        Assert.Equal(0, first[1].ArrivalDelay!.Value, 2);
        Assert.Equal(-1 / 3.0, first[1].DepartureDelay!.Value, 2);
        Assert.Equal(StationOutcome.Missed, first[2].Outcome);
        Assert.Equal(StationOutcome.Stopped, StationProgress.Of(r, 120)[1].Outcome);
    }

    [Fact]
    public void The_delay_follows_the_train_from_station_to_station()
    {
        var r = Journey();

        Assert.Null(StationProgress.DelayAt(r, 0.5));
        var standing = StationProgress.DelayAt(r, 65)!;
        Assert.True(standing.Standing);
        Assert.Equal("Stacja_B", standing.Station);
        Assert.Equal(0, standing.Minutes, 2);
        var left = StationProgress.DelayAt(r, 80)!;
        Assert.False(left.Standing);
        Assert.Equal(-1 / 3.0, left.Minutes, 2);
        // At D, arrived at 10:35:55 for 10:40: early, and nothing to depart by.
        var second = StationProgress.DelayAt(r, 118)!;
        Assert.Equal("ROJ2", second.Train);
        Assert.True(second.Minutes < -4);

        // Left B at 10:35:10, C planned 10:37:30: by 10:35:50 (t = 110 of ROJ1's time) not yet overdue.
        Assert.Equal(-1 / 3.0, StationProgress.DelayAt(r, 90)!.Minutes, 2);

        var delays = StationProgress.StationDelays(r, 80);
        Assert.Equal(-1 / 3.0, delays["Stacja B"], 2);
    }

    [Fact]
    public void The_names_of_the_stations_are_shown_without_what_tells_their_stop_points_apart()
    {
        Assert.Equal("Bałtyk Główny", StationNames.Display("Bałtyk_Główny#2"));
        Assert.Equal("Turów", StationNames.Display("Turów"));
        Assert.Equal("Psie Pole po", StationNames.Display("Psie_Pole_po#"));
    }

    [Fact]
    public void The_horn_counts_when_it_sounded_after_the_horn_point()
    {
        var checks = Journey().UserHornChecks().ToList();

        Assert.Equal(2, checks.Count);
        Assert.True(checks[0].Sounded);
        Assert.False(checks[1].Sounded);
    }

    [Fact]
    public void The_camera_is_known_from_its_first_record()
    {
        var r = Journey();

        Assert.Null(r.CameraAt(-1));
        Assert.Equal(CameraMode.Cab, r.CameraAt(49)!.Value.Mode);
        Assert.Equal(CameraMode.Free, r.CameraAt(51)!.Value.Mode);
    }

    [Fact]
    public void The_summary_counts_the_stops_of_every_timetable_the_horns_and_the_protection()
    {
        var summary = Journey().Summarize();

        Assert.Equal(2, summary.Stops);
        Assert.Equal(1, summary.StopsMissed);
        Assert.Equal(2, summary.HornPoints);
        Assert.Equal(1, summary.HornsMissed);
        Assert.Equal(1, summary.SecurityBrakings);
        // Taken over half a second, as the lateral acceleration jumps from sample to sample.
        Assert.Equal(1.2f, summary.MaxLateralAcceleration, 3);
    }
}

public class TrainDelayTests
{
    [Fact]
    public void A_train_held_on_the_way_grows_late_by_the_time_of_the_next_station()
    {
        // Clock 10:34 at t = 0. Departs A at 10:35 (t = 60) on time, held short of B planned at 10:37.
        var f = new BlackBoxFile().Info()
            .Timetable(0, 1, "ROJ9", "A", "B", ("A", -1, 635), ("B", 637, 638))
            .Station(1, 1, 1, 1, "A", 1150);
        for (double t = 0; t <= 400; t += 0.5)
            f.Cab(t, 1, 1150 + Math.Max(0, Math.Min(t, 90) - 60) * 10, t is > 60 and < 90 ? 40 : 0);
        var r = BlackBoxReader.Read(f.End(400).Stream());

        Assert.Equal(0, StationProgress.DelayAt(r, 100)!.Minutes, 1);
        // 10:40 at t = 360: three minutes past B's arrival, and not there.
        var held = StationProgress.DelayAt(r, 360)!;
        Assert.False(held.Standing);
        Assert.Equal(3, held.Minutes, 2);
    }
}

/// <summary>A train given its next timetable, one recording holding both.</summary>
public class NextTimetableTests
{
    [Fact]
    public void A_station_written_just_before_the_next_timetable_goes_with_it()
    {
        // ROJ1 stops at A and ends at B; at B it is given ROJ2, which starts there. A recording made before
        // the simulator wrote the timetable first has B of ROJ2 half a second ahead of ROJ2 itself (here left behind,
        // which on ROJ1 would mark A missed).
        var f = new BlackBoxFile().Info()
            .Timetable(0, 1, "ROJ1", "A", "B", ("A", -1, 635), ("B", 637, -1))
            .Station(1, 1, 1, 1, "A", 1150)
            .Station(50, 1, 1, 2, "B", 1150)
            .Station(99.5, 1, 3, 1, "B", 1150)
            .Timetable(100, 1, "ROJ2", "B", "C", ("B", -1, 640), ("C", 645, -1));
        for (double t = 0; t <= 120; t += 0.5)
            f.Cab(t, 1, 1150, 0);
        var r = BlackBoxReader.Read(f.End(120).Stream());

        var first = StationProgress.Of(r, r.Timetables[0]);
        Assert.Equal(StationOutcome.Stopped, first[0].Outcome);
        Assert.Equal(1, first[0].Event!.Time);
        var second = StationProgress.Of(r, r.Timetables[1]);
        Assert.Equal(StationOutcome.Missed, second[0].Outcome);
        Assert.Equal(99.5, second[0].Event!.Time);
    }
}
