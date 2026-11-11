using System.Linq;
using Eu07.BlackBox;

namespace StarterNG.Tests.BlackBox;

/// <summary>The light signals at the head and the tail of the user's train, against those it was to show.</summary>
public class HeadCodeTests
{
    [Theory]
    [InlineData(Lights.HeadlightLeft | Lights.HeadlightRight | Lights.HeadlightUpper, HeadCode.Pc1)]
    [InlineData(Lights.HeadlightLeft | Lights.HeadlightRight | Lights.HeadlightUpper | Lights.HighBeamLeft | Lights.HighBeamRight, HeadCode.Pc1)]
    [InlineData(Lights.RedMarkerLeft | Lights.HeadlightRight | Lights.HeadlightUpper, HeadCode.Pc2)]
    [InlineData(Lights.RedMarkerLeft | Lights.RedMarkerRight, HeadCode.Pc5)]
    [InlineData(Lights.RearEndSignals, HeadCode.Pc5)]
    [InlineData(Lights.HeadlightRight, HeadCode.Tb1)]
    [InlineData(Lights.HeadlightLeft, HeadCode.Tb1)]
    [InlineData(Lights.None, HeadCode.Off)]
    [InlineData(Lights.HeadlightLeft | Lights.HeadlightRight, HeadCode.Other)]
    public void The_lamps_lit_tell_the_signal(Lights lights, HeadCode code) => Assert.Equal(code, HeadCodes.Of(lights));

    private const ushort Pc1 = 0x15, Pc5 = 0x22, Tb1 = 0x10;

    private static Recording Drive(params (double From, double To, ushort Head, ushort Tail, byte Movement, short HintHead)[] stretches)
    {
        var f = new BlackBoxFile().Info();
        foreach (var (from, to, head, tail, movement, hint) in stretches)
            for (double t = from; t < to; t += 0.5)
                f.Cab(t, 1, 1000 + t * 10, 40, lightsHead: head, lightsTail: tail, movement: movement, hintHead: hint);
        return BlackBoxReader.Read(f.End(stretches[^1].To).Stream());
    }

    [Fact]
    public void A_train_run_is_to_show_Pc1_and_Pc5()
    {
        var summary = Drive((0, 60, Pc1, Pc5, 1, -1)).Summarize();
        Assert.True(summary.LightsJudgedSeconds > 55);
        Assert.Equal(0, summary.WrongLightsSeconds);
        Assert.Empty(summary.LightsFaults);
    }

    [Fact]
    public void Shunting_lights_on_a_train_run_count_as_wrong_once_for_the_whole_stretch()
    {
        var summary = Drive((0, 30, Pc1, Pc5, 1, -1), (30, 60, Tb1, Tb1, 1, -1), (60, 90, Pc1, Pc5, 1, -1)).Summarize();
        Assert.Equal(30, summary.WrongLightsSeconds, 0);
        var fault = Assert.Single(summary.LightsFaults);
        Assert.Equal(HeadCode.Tb1, fault.Head);
        Assert.Equal(HeadCode.Pc1, fault.HeadRequired);
        Assert.True(fault.HeadWrong && fault.TailWrong);
    }

    [Fact]
    public void Shunting_is_to_show_Tb1()
    {
        var summary = Drive((0, 30, Tb1, Tb1, 2, -1), (30, 40, Pc1, Pc5, 2, -1)).Summarize();
        var fault = Assert.Single(summary.LightsFaults);
        Assert.Equal(HeadCode.Tb1, fault.HeadRequired);
    }

    [Fact]
    public void The_pattern_the_timetable_asks_for_is_the_one_to_show()
    {
        // Pc 2 asked for at the head: the left red marker, the right headlight and the upper one.
        const short pc2 = 0x16;
        var summary = Drive((0, 30, Pc1, Pc5, 1, pc2)).Summarize();
        var fault = Assert.Single(summary.LightsFaults);
        Assert.Equal(HeadCode.Pc2, fault.HeadRequired);
        Assert.False(fault.TailWrong);
    }

    private static Recording DriveWith(ushort head, ushort tail, ushort equippedHead, ushort equippedTail, byte movement = 1, byte driving = 1)
    {
        var f = new BlackBoxFile().Info();
        for (double t = 0; t < 30; t += 0.5)
            f.Cab(t, 1, 1000 + t * 10, 40, driving: driving, lightsHead: head, lightsTail: tail, movement: movement,
                  equippedHead: equippedHead, equippedTail: equippedTail);
        return BlackBoxReader.Read(f.End(30).Stream());
    }

    [Fact]
    public void The_lamps_the_vehicles_have_are_read()
    {
        var sample = DriveWith(Pc1, Pc5, 0x1B5, 0x22).Cab[0];
        Assert.Equal((Lights)0x1B5, sample.EquippedHead);
        Assert.Equal((Lights)0x22, sample.EquippedTail);
    }

    [Theory]
    // without the upper headlight Pc 1 is the two others
    [InlineData(0x11, 0x11)]
    // with auxiliary lamps, those and the upper headlight
    [InlineData(0x184, 0x1B5)]
    // a vehicle with no lamps for it: the rear-end signals
    [InlineData(0x40, 0x40)]
    public void Pc1_is_what_the_vehicle_can_show_of_it(ushort lit, ushort equipped)
    {
        var summary = DriveWith(lit, Pc5, equipped, Pc5).Summarize();
        Assert.True(summary.LightsJudgedSeconds > 25);
        Assert.Empty(summary.LightsFaults);
    }

    [Fact]
    public void A_tail_with_the_rear_end_signals_only_shows_Pc5_with_them()
    {
        Assert.Empty(DriveWith(Pc1, 0x40, Pc1, 0x40).Summarize().LightsFaults);
        var fault = Assert.Single(DriveWith(Pc1, 0x00, Pc1, 0x40).Summarize().LightsFaults);
        Assert.False(fault.HeadWrong);
        Assert.True(fault.TailWrong);
    }

    [Fact]
    public void A_vehicle_with_all_the_lamps_is_to_light_all_three_for_Pc1()
    {
        var fault = Assert.Single(DriveWith(0x11, Pc5, 0x35, Pc5).Summarize().LightsFaults);
        Assert.True(fault.HeadWrong);
        Assert.Equal(HeadCode.Pc1, fault.HeadRequired);
    }

    [Fact]
    public void The_lights_the_autopilot_sets_are_not_the_users()
    {
        var summary = DriveWith(Tb1, Tb1, Pc1, Pc5, driving: 2).Summarize();
        Assert.Equal(0, summary.LightsJudgedSeconds);
        Assert.Empty(summary.LightsFaults);
    }

    [Fact]
    public void An_older_recording_reads_a_pattern_asked_for_as_the_signal_it_shows()
    {
        // Pc 5 asked for at the tail, the rear-end signals lit, no lamps written: Pc 5 as before
        var f = new BlackBoxFile().Info();
        for (double t = 0; t < 30; t += 0.5)
            f.Cab(t, 1, 1000 + t * 10, 40, lightsHead: Pc1, lightsTail: 0x40, movement: 1, hintTail: 0x22);
        var summary = BlackBoxReader.Read(f.End(30).Stream()).Summarize();
        Assert.True(summary.LightsJudgedSeconds > 25);
        Assert.Empty(summary.LightsFaults);
    }

    [Fact]
    public void An_older_recording_tells_nothing_of_the_signals()
    {
        var f = new BlackBoxFile().Info();
        for (double t = 0; t < 30; t += 0.5)
            f.Cab(t, 1, 1000 + t * 10, 40, lightsHead: Tb1);
        var summary = BlackBoxReader.Read(f.End(30).Stream()).Summarize();
        Assert.Equal(0, summary.LightsJudgedSeconds);
    }
}

/// <summary>The user going over to another vehicle: the timetable of the train is the one of the train the user is in.</summary>
public class UserVehicleTests
{
    private const byte Coupled = 0x83;

    [Fact]
    public void Another_vehicle_of_the_same_train_keeps_its_timetable_another_train_has_none()
    {
        // The user in vehicle 1 of the unit 1-2 with ROJ1; at t = 50 over to the other cab, at t = 100 into vehicle 9, on its own.
        var f = new BlackBoxFile().Info()
            .Coupling(0, 1, 1, 2, 0, Coupled, 0, 0)
            .Timetable(0, 1, "ROJ1", "A", "B", ("A", -1, 635), ("B", 640, -1));
        for (double t = 0; t < 150; t += 0.5)
            f.Cab(t, t < 50 ? 1u : t < 100 ? 2u : 9u, 1000, 0);
        var r = BlackBoxReader.Read(f.End(150).Stream());

        Assert.Equal("ROJ1", r.TimetableAt(20)!.Train);
        Assert.Equal("ROJ1", r.TimetableAt(70)!.Train);
        Assert.Null(r.TimetableAt(120));
    }

    [Fact]
    public void Single_vehicles_with_no_coupling_written_are_still_told_apart()
    {
        // A recording made with the movement written, where no train is longer than one vehicle: no coupling at all.
        var f = new BlackBoxFile().Info()
            .Timetable(0, 1, "ROJ1", "A", "B", ("A", -1, 635), ("B", 640, -1));
        for (double t = 0; t < 100; t += 0.5)
            f.Cab(t, t < 50 ? 1u : 9u, 1000, 0, movement: 1);
        var r = BlackBoxReader.Read(f.End(100).Stream());

        Assert.Equal("ROJ1", r.TimetableAt(20)!.Train);
        Assert.Null(r.TimetableAt(70));
    }

    [Fact]
    public void An_older_recording_with_no_coupling_keeps_the_timetable()
    {
        var f = new BlackBoxFile().Info()
            .Timetable(0, 1, "ROJ1", "A", "B", ("A", -1, 635), ("B", 640, -1));
        for (double t = 0; t < 100; t += 0.5)
            f.Cab(t, t < 50 ? 1u : 2u, 1000, 0);
        var r = BlackBoxReader.Read(f.End(100).Stream());

        Assert.Equal("ROJ1", r.TimetableAt(70)!.Train);
    }
}

/// <summary>The departures and the platforms in the summary.</summary>
public class StationSummaryTests
{
    [Fact]
    public void The_summary_tells_the_largest_delay_leaving_and_the_stops_off_the_platform()
    {
        // Clock 10:34 at t = 0, leaves A at once. Stops at B (10:36 to 10:37) from t = 120, leaves at t = 240 (10:38): a minute late.
        var f = new BlackBoxFile().Info()
            .Timetable(0, 1, "ROJ1", "A", "C", ("A", -1, 634), ("B", 636, 637), ("C", 640, -1))
            .Station(1, 1, 1, 1, "A", 1000)
            .Station(120, 1, 1, 2, "Stacja_B", 1000, distance: 74.7f, trainLength: 129.8f, platform: 201);
        for (double t = 0; t < 300; t += 0.5)
            f.Cab(t, 1, 1000, t is >= 120 and < 240 or < 2 ? 0 : 40);
        var r = BlackBoxReader.Read(f.End(300).Stream());
        var summary = r.Summarize();

        Assert.Equal(1, summary.MaxDepartureDelayMinutes!.Value, 1);
        var off = Assert.Single(summary.OffPlatform);
        Assert.Equal("B", off.Station);
        Assert.Equal(3.5, off.Metres, 1);
    }
}
