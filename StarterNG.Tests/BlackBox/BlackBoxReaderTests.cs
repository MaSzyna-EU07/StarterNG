using System.Numerics;
using Eu07.BlackBox;

namespace StarterNG.Tests.BlackBox;

public class BlackBoxReaderTests
{
    private static BlackBoxFile Session() => new BlackBoxFile()
        .Info()
        .Track(0, "start", new Vector3(5, 0.4f, 1100), new Vector3(5, 0.4f, 1200))
        .Track(1, "zwr01", 1, 0, new Vector3(5, 0.4f, 1200), new Vector3(5, 0.4f, 1234))
        .Track(1, "zwr01", 1, 1, new Vector3(5, 0.4f, 1200), new Vector3(8, 0.4f, 1234))
        .Signal(0, "stacja1_a", 2.5, 3.0, 1300)
        .Vehicle(1, "ep07-bb", "ROJ65113")
        .Vehicle(2, "bdnu")
        .Switch(0, 1, 0)
        .Aspect(0, 0, 0, -1)
        .Clock(0, 10 * 3600 + 34 * 60)
        .User(0, 1, true)
        .Vehicles(0, true, (1, 5, 1150, 0), (2, 5, 1130, 0))
        .Cab(0.1, 1, 1150, 0)
        .Command(1.0, 1, true, "aidriverenable")
        .Command(1.1, 1, false, "aidriverenable")
        .Driver(1.0, 1, 2)
        .Aspect(2.0, 0, 40, -1)
        .Switch(3.0, 1, 1)
        .Passed(4.0, 1, 0, "stacja1_a_sem", 40, 38.5f, 0x01)
        .Removed(5.0, 2)
        .End(6.0);

    [Fact]
    public void Every_record_of_a_session_is_read_as_the_simulator_wrote_it()
    {
        var r = BlackBoxReader.Read(Session().Stream());

        Assert.Equal(1, r.Version);
        Assert.Equal("td.scn", r.Info!.Scenery);
        Assert.Equal(new TimeSpan(10, 34, 0), r.Info.StartClock);
        Assert.Equal(60f, r.Info.KeyframeInterval);
        Assert.Equal(3, r.Tracks.Count);
        Assert.Equal(2, r.TrackWays(1).Count);
        Assert.Equal(TrackKind.SwitchBranch, r.TrackWays(1)[1].Kind);
        Assert.Equal(new Vector3(8, 0.4f, 1234), r.TrackWays(1)[1].Points[1]);
        Assert.Equal("stacja1_a", r.Signal(0)!.Name);
        Assert.Equal(1300, r.Signal(0)!.Position.Z);
        Assert.Equal("ROJ65113", r.Vehicle(1)!.Train);
        Assert.Equal(5.0, r.Vehicle(2)!.RemovedAt);

        var cab = Assert.Single(r.Cab);
        Assert.Equal(1, cab.Vehicle);
        Assert.Equal(new Position(5, 0.4, 1150), cab.Position);
        Assert.Equal(Driver.Human, cab.Driving);
        Assert.True(cab.AiValid);
        Assert.Equal(3, cab.MainController);
        Assert.Equal(5.0f, cab.BrakePipe);
        Assert.Equal(40f, cab.Ai.TargetKmh);
        Assert.Equal(60f, cab.Ai.TimetableVmaxKmh);
        Assert.False(cab.Ai.HasNextLimit);
        Assert.Equal(CabDevices.Battery | CabDevices.MainBreaker | CabDevices.PantographFront, cab.Devices);
        Assert.True(cab.Has(CabDevices.MainBreaker));
        Assert.False(cab.Has(CabDevices.Compressor));
        Assert.Equal(8.5f, cab.MainReservoir);
        Assert.Equal(3000f, cab.Voltage);
        Assert.Equal(1100f, cab.EngineRpm);
        Assert.Equal(Lights.HeadlightLeft | Lights.HeadlightUpper | Lights.HeadlightRight, cab.LightsHead);
        Assert.Equal(Lights.RedMarkerLeft | Lights.RedMarkerRight, cab.LightsTail);

        Assert.Equal(2, r.Commands.Count);
        Assert.Equal("aidriverenable", r.Commands[0].Name);
        Assert.True(r.Commands[0].Pressed);
        Assert.Equal(2.0, r.Commands[0].Param2);
        Assert.Equal(Driver.Ai, Assert.Single(r.DriverChanges).Driving);
        var passed = Assert.Single(r.Passings);
        Assert.Equal("stacja1_a_sem", passed.Event);
        Assert.Equal(PassingFlags.Shunt, passed.Flags);
        Assert.Equal(38.5f, passed.SpeedKmh);
        Assert.False(passed.AtDanger);
        Assert.True(r.Complete);
        Assert.Equal(6.0, r.Duration);
        Assert.Equal(0, r.Unknown);
        Assert.Equal(0, r.Malformed);
    }

    [Fact]
    public void The_switches_and_the_aspects_answer_for_any_moment()
    {
        var r = BlackBoxReader.Read(Session().Stream());

        Assert.Equal(0, r.SwitchAt(1, 2.9));
        Assert.Equal(1, r.SwitchAt(1, 3.0));
        Assert.Equal(-1, r.SwitchAt(7, 3.0));
        Assert.Equal(0f, r.AspectAt(0, 1.9)!.Value.Speed1);
        Assert.Equal(40f, r.AspectAt(0, 2.0)!.Value.Speed1);
        Assert.Null(r.AspectAt(3, 2.0));
    }

    [Fact]
    public void A_file_still_written_is_read_on_from_where_the_last_read_stopped()
    {
        var bytes = Session().Bytes;
        var whole = BlackBoxReader.Read(new MemoryStream(bytes));

        // Cut at every byte: a record cut short waits for the rest, nothing is read twice.
        for (int cut = 0; cut <= bytes.Length; cut++)
        {
            var reader = new BlackBoxReader();
            reader.ReadMore(new MemoryStream(bytes, 0, cut));
            Assert.False(reader.Recording.Complete && cut < bytes.Length);
            reader.ReadMore(new MemoryStream(bytes));

            Assert.Equal(whole.Tracks.Count, reader.Recording.Tracks.Count);
            Assert.Equal(whole.Commands.Count, reader.Recording.Commands.Count);
            Assert.Equal(whole.Cab.Count, reader.Recording.Cab.Count);
            Assert.Equal(whole.Aspects.Count, reader.Recording.Aspects.Count);
            Assert.True(reader.Recording.Complete);
            Assert.Equal(bytes.Length, reader.Offset);
        }
    }

    [Fact]
    public void A_record_of_a_later_version_is_passed_over_by_its_length()
    {
        var file = new BlackBoxFile().Info()
                                     .Record(0x7e, w => w.Write(new byte[40]))
                                     .Command(1.0, 1, true, "hornhighactivate")
                                     .End(2.0);

        var r = BlackBoxReader.Read(file.Stream());

        Assert.Equal(1, r.Unknown);
        Assert.Equal("hornhighactivate", Assert.Single(r.Commands).Name);
        Assert.True(r.Complete);
    }

    [Fact]
    public void A_record_shorter_than_its_type_is_passed_over_and_counted()
    {
        var file = new BlackBoxFile().Record(0x20, w => w.Write(1.0)).Clock(1.0, 3600).End(2.0);

        var r = BlackBoxReader.Read(file.Stream());

        Assert.Empty(r.Cab);
        Assert.Equal(1, r.Malformed);
        Assert.Single(r.Clocks);
    }

    [Fact]
    public void Texts_longer_than_a_byte_can_count_and_letters_beyond_ascii_read_back()
    {
        string name = "żółw_" + new string('x', 300);
        var r = BlackBoxReader.Read(new BlackBoxFile().Signal(0, name, 0, 0, 0).Signal(1, "", 1, 1, 1).Stream());

        Assert.Equal(name, r.Signal(0)!.Name);
        Assert.Equal("", r.Signal(1)!.Name);
    }

    [Fact]
    public void Something_else_than_a_black_box_or_a_version_not_known_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => BlackBoxReader.Read(new MemoryStream("Time[s] Velocity[m/s]\r\n"u8.ToArray())));
        Assert.Throws<InvalidDataException>(() => BlackBoxReader.Read(new BlackBoxFile(version: 2).Stream()));
    }

    [Fact]
    public void A_file_written_anew_under_the_reader_is_told_so()
    {
        var reader = new BlackBoxReader();
        reader.ReadMore(Session().Stream());

        Assert.Throws<InvalidDataException>(() => reader.ReadMore(new BlackBoxFile().Info().Stream()));
    }

    [Fact]
    public void A_vehicle_listed_only_when_it_moved_stands_until_the_look_before_it_was_seen_moving()
    {
        var r = BlackBoxReader.Read(new BlackBoxFile()
                                    .Info()
                                    .Vehicle(2, "et22")
                                    .Vehicles(0, true, (2, 0, 0, 0))
                                    .Vehicles(60, true, (2, 0, 0, 0))
                                    .Vehicles(90, false, (2, 0, 10, 36))
                                    .Vehicles(91, false, (2, 0, 20, 36))
                                    .Stream());

        Assert.Equal(0f, r.VehiclesAt(30).Single().Position.Z);
        // Not listed from 60 to 90: it stood until 88.5, the look interval and a half before.
        Assert.Equal(0f, r.VehiclesAt(88).Single().Position.Z);
        Assert.InRange(r.VehiclesAt(89.25).Single().Position.Z, 4.9f, 5.1f);
        Assert.InRange(r.VehiclesAt(90.5).Single().Position.Z, 14.9f, 15.1f);
        Assert.Equal(20f, r.VehiclesAt(200).Single().Position.Z);
    }

    [Fact]
    public void A_vehicle_removed_is_on_the_map_no_more()
    {
        var r = BlackBoxReader.Read(Session().Stream());

        Assert.Equal(2, r.VehiclesAt(4.9).Count);
        Assert.Equal(1, Assert.Single(r.VehiclesAt(5.0)).Id);
    }

    [Fact]
    public void The_clock_goes_on_from_the_last_tick_and_over_midnight()
    {
        var r = BlackBoxReader.Read(new BlackBoxFile()
                                    .Info(secondOfDay: 86390)
                                    .Clock(0, 86390)
                                    .Clock(1, 86391)
                                    .Stream());

        Assert.Equal(new TimeSpan(23, 59, 51), r.ClockAt(1));
        Assert.Equal(86395.5, r.ClockAt(5.5).TotalSeconds, 3);
        Assert.Equal(1.0, r.ClockAt(11).TotalSeconds, 3);
    }

    [Fact]
    public void The_position_of_the_train_runs_between_its_samples_and_not_across_a_gap()
    {
        var r = BlackBoxReader.Read(new BlackBoxFile()
                                    .Cab(0.0, 1, 100, 36)
                                    .Cab(0.1, 1, 101, 36)
                                    .Cab(5.0, 1, 200, 36)
                                    .Stream());

        Assert.Equal(100.5, r.CabPositionAt(0.05)!.Value.Z, 6);
        Assert.Null(r.CabPositionAt(3.0));
        Assert.Null(r.CabPositionAt(-1));
    }
}
