using StarterNG.Application;
using StarterNG.Domain.Tapes;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class TapeLaunchTests
{
    private static readonly DateTime Launched = new(2025, 12, 31, 23, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_launch_is_written_and_read_back_with_names_that_hold_spaces()
    {
        var launch = new TapeLaunch(Launched, "td.scn", new TimeSpan(7, 52, 0), new[]
        {
            new TapeVehicle("EP07-424-HIST", "pkp/ep07_v1", "ep07-424"),
            new TapeVehicle("wagon 1", "pkp/111a_v2", "111a")
        });

        var back = TapeLaunch.Parse(launch.Format())!;

        Assert.Equal(Launched, back.LaunchedUtc);
        Assert.Equal("td.scn", back.Scenery);
        Assert.Equal(new TimeSpan(7, 52, 0), back.StartClock);
        Assert.Equal("pkp/ep07_v1", back.Find("ep07-424-hist")!.DataFolder);
        Assert.Equal("111a", back.Find("wagon 1")!.TypeName);
    }

    [Fact]
    public void The_driven_vehicle_and_the_consist_are_written_and_read_back()
    {
        var launch = new TapeLaunch(Launched, "td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(),
                                    "EP07 424", new[] { "EP07 424", "wagon 1" });

        var back = TapeLaunch.Parse(launch.Format())!;

        Assert.Equal(TapeRole.Driven, back.RoleOf("ep07 424"));
        Assert.Equal(TapeRole.OwnConsist, back.RoleOf("WAGON 1"));
        Assert.Equal(TapeRole.Other, back.RoleOf("et22-100"));
    }

    [Fact]
    public void A_note_without_the_driven_vehicle_tells_nothing_of_it()
    {
        var back = TapeLaunch.Parse("launched\t2025-12-31T23:00:00.0000000Z\nscenery\ttd.scn\nclock\t10:30\n")!;

        Assert.Null(back.Driven);
        Assert.Equal(TapeRole.Other, back.RoleOf("ep07-424"));
        Assert.Null(back.SceneryName);
        Assert.Null(back.Train);
    }

    [Fact]
    public void The_scenario_and_the_train_are_written_and_read_back()
    {
        var launch = new TapeLaunch(Launched, "td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(),
                                    SceneryName: "Tor doświadczalny", Train: "EIE 1531");

        var back = TapeLaunch.Parse(launch.Format())!;

        Assert.Equal("Tor doświadczalny", back.SceneryName);
        Assert.Equal("EIE 1531", back.Train);
    }

    [Fact]
    public void A_tape_of_the_last_launch_gets_its_scenario_and_train()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("physicslog", "EP07-424.dat"), "t"));
        installation.Clock.Now = Launched;
        var tapes = installation.Tapes;

        tapes.RecordLaunch("td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "EP07-424", new[] { "EP07-424" },
                           "Tor doświadczalny", "EIE1531");
        var context = tapes.ContextOf(tapes.List().Single());

        Assert.Equal("Tor doświadczalny", context.Scenery);
        Assert.Equal("EIE1531", context.Train);
    }

    [Fact]
    public void A_tape_of_someone_elses_vehicle_does_not_get_the_train_of_the_player()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("physicslog", "ET22-100.dat"), "t"));
        installation.Clock.Now = Launched;
        var tapes = installation.Tapes;

        tapes.RecordLaunch("td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "EP07-424", new[] { "EP07-424" },
                           "Tor doświadczalny", "EIE1531");
        var context = tapes.ContextOf(tapes.List().Single());

        Assert.Equal("Tor doświadczalny", context.Scenery);
        Assert.Null(context.Train);
    }

    [Fact]
    public void A_scenario_without_a_name_goes_by_its_file()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("physicslog", "EP07-424.dat"), "t"));
        installation.Clock.Now = Launched;
        var tapes = installation.Tapes;

        tapes.RecordLaunch("td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>());

        Assert.Equal("td.scn", tapes.ContextOf(tapes.List().Single()).Scenery);
    }

    [Theory]
    [InlineData("[ Rodzaj i numer pociagu                 | EIE1531    \t      ] \r\n[ Kategoria | IC ]\r\n", "EIE1531")]
    [InlineData(" ____\r\n[ Rodzaj i numer pociągu  |  LPE 782     ]\r\n", "LPE 782")]
    [InlineData("[ Kategoria | IC ]\r\n", null)]
    [InlineData("", null)]
    public void The_train_number_comes_from_the_heading_of_its_timetable(string timetable, string? number)
    {
        var files = new InMemoryFileSystem().WithFile(TestInstallation.At("scenery", "baltyk", "eie1531.txt"), timetable);

        Assert.Equal(number, new StarterNG.Infrastructure.Sceneries.TimetableLocator(files)
                         .TrainNumber(new StarterNG.Domain.Sceneries.Scenery(TestInstallation.At("scenery", "td.scn"), ""),
                                      "baltyk/eie1531"));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("NONE")]
    [InlineData("")]
    [InlineData(null)]
    public void A_trainset_without_a_timetable_has_no_train_number(string? name) =>
        Assert.Null(new StarterNG.Infrastructure.Sceneries.TimetableLocator(new InMemoryFileSystem())
                        .TrainNumber(new StarterNG.Domain.Sceneries.Scenery(TestInstallation.At("scenery", "td.scn"), ""), name));

    [Fact]
    public void A_timetable_that_is_not_there_gives_no_train_number() =>
        Assert.Null(new StarterNG.Infrastructure.Sceneries.TimetableLocator(new InMemoryFileSystem())
                        .TrainNumber(new StarterNG.Domain.Sceneries.Scenery(TestInstallation.At("scenery", "td.scn"), ""),
                                     @"baltyk\mod\eie1531"));

    [Fact]
    public void The_driven_tape_and_the_consist_are_listed_ahead_of_the_rest()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("physicslog", "a-other.dat"), "t")
            .WithFile(TestInstallation.At("physicslog", "b-wagon.dat"), "t")
            .WithFile(TestInstallation.At("physicslog", "c-lok.dat"), "t"));
        installation.Clock.Now = Launched;
        var tapes = installation.Tapes;

        tapes.RecordLaunch("td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "c-lok", new[] { "c-lok", "b-wagon" });

        Assert.Equal("c-lok", tapes.DrivenVehicle);
        Assert.Equal(new[] { "c-lok", "b-wagon", "a-other" }, tapes.List().Select(tape => tape.Vehicle));
        Assert.Equal(new[] { TapeRole.StartedIn, TapeRole.OwnConsist, TapeRole.Other }, tapes.List().Select(tape => tape.Role));
    }

    [Fact]
    public void With_no_log_the_vehicle_of_the_launch_is_only_the_one_it_started_in()
    {
        var launch = new TapeLaunch(Launched, "td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "ra", new[] { "ra", "rb" });

        Assert.Equal(TapeRole.StartedIn, launch.RoleOf("ra", PlayerVehicles.Unknown));
        Assert.Equal(TapeRole.OwnConsist, launch.RoleOf("rb", PlayerVehicles.Unknown));
    }

    [Fact]
    public void Where_the_player_has_been_is_written_and_read_back()
    {
        var launch = new TapeLaunch(Launched, "td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(),
                                    Player: PlayerVehicles.None.After("a received command: [x]\nb received command: [x]\n"));

        var back = TapeLaunch.Parse(launch.Format())!;

        Assert.Equal("b", back.Player!.Current);
        Assert.True(back.Player.Used("a"));
    }

    [Fact]
    public void A_note_of_a_single_launch_still_reads()
    {
        var all = TapeLaunch.ParseAll("launched\t2025-12-31T23:00:00.0000000Z\nscenery\ttd.scn\nclock\t10:30\n");

        Assert.Equal("td.scn", all.Single().Scenery);
        Assert.Null(all.Single().Player);
    }

    [Fact]
    public void Tapes_part_between_the_launches_they_were_written_after()
    {
        var files = new InMemoryFileSystem()
            .WithFile(TestInstallation.At("physicslog", "ra.dat"), "t")
            .WithFile(TestInstallation.At("physicslog", "rb.dat"), "t");
        var installation = new TestInstallation(files);
        var tapes = installation.Tapes;
        installation.Clock.Now = Launched;
        tapes.RecordLaunch("a.scn", new TimeSpan(7, 0, 0), Array.Empty<TapeVehicle>(), "ra", new[] { "ra" }, "A", "T1");
        installation.Clock.Now = Launched.AddHours(1);
        tapes.RecordLaunch("b.scn", new TimeSpan(9, 0, 0), Array.Empty<TapeVehicle>(), "rb", new[] { "rb" }, "B", "T2");
        files.WrittenAt(TestInstallation.At("physicslog", "ra.dat"), Launched.AddMinutes(30))
             .WrittenAt(TestInstallation.At("physicslog", "rb.dat"), Launched.AddMinutes(90));

        var sessions = tapes.Sessions();

        Assert.Equal(new[] { "B", "A" }, sessions.Select(session => session.Launch!.SceneryName));
        Assert.Equal("rb", sessions[0].Tapes.Single().Vehicle);
        Assert.Equal("ra", sessions[1].Tapes.Single().Vehicle);
        Assert.Equal(new TimeSpan(7, 0, 0), tapes.ContextOf(sessions[1].Tapes[0]).StartClock);
        Assert.Equal("T1", tapes.ContextOf(sessions[1].Tapes[0]).Train);
    }

    [Fact]
    public void Tapes_with_no_launch_noted_go_together_by_when_they_were_written()
    {
        var early = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("physicslog", "a.dat"), "t")
            .WithFile(TestInstallation.At("physicslog", "b.dat"), "t")
            .WithFile(TestInstallation.At("physicslog", "c.dat"), "t")
            .WrittenAt(TestInstallation.At("physicslog", "a.dat"), early)
            .WrittenAt(TestInstallation.At("physicslog", "b.dat"), early.AddSeconds(30))
            .WrittenAt(TestInstallation.At("physicslog", "c.dat"), early.AddHours(1)));

        var sessions = installation.Tapes.Sessions();

        Assert.Equal(new[] { 1, 2 }, sessions.Select(session => session.Tapes.Count));
        Assert.All(sessions, session => Assert.Null(session.Launch));
    }

    [Fact]
    public void A_running_session_is_listed_with_no_tapes_yet_and_goes_with_the_simulator()
    {
        var installation = new TestInstallation(new InMemoryFileSystem());
        installation.Clock.Now = Launched;
        var tapes = installation.Tapes;
        tapes.RecordLaunch("td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "ra");

        installation.SimulatorRunning = true;
        var running = tapes.Sessions().Single();
        Assert.True(running.Running);
        Assert.Empty(running.Tapes);

        installation.SimulatorRunning = false;
        Assert.Empty(tapes.Sessions());
    }

    [Fact]
    public void A_log_of_its_own_tells_of_the_vehicles_when_the_common_one_is_stale()
    {
        string kept = TestInstallation.At("logs", "log (td.scn) 2026.txt");
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("log.txt"), "ra received command: [x]\n")
            .WithFile(kept, "rb received command: [x]\n")
            .WrittenAt(TestInstallation.At("log.txt"), Launched.AddHours(-2))
            .WrittenAt(kept, Launched.AddMinutes(10)));
        installation.Clock.Now = Launched;
        var tapes = installation.Tapes;

        tapes.RecordLaunch("td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "ra", new[] { "ra", "rb" });

        Assert.Equal("rb", tapes.DrivenVehicle);
    }

    [Fact]
    public void The_vehicles_of_a_run_stay_with_it_when_the_next_begins()
    {
        var (tapes, installation) = TapesOfTrain("ra received command: [x]\nrb received command: [x]\n");
        var written = Launched.AddMinutes(30);
        installation.Files.WrittenAt(TestInstallation.At("log.txt"), written)
                    .WrittenAt(TestInstallation.At("physicslog", "ra.dat"), written)
                    .WrittenAt(TestInstallation.At("physicslog", "rb.dat"), written)
                    .WrittenAt(TestInstallation.At("physicslog", "obcy.dat"), written);

        installation.Clock.Now = Launched.AddHours(1);
        tapes.RecordLaunch("td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "inny", new[] { "inny" });

        var run = tapes.Sessions().Single();
        Assert.Equal("Test", run.Launch!.SceneryName);
        Assert.Equal(TapeRole.Driven, run.Tapes.Single(tape => tape.Vehicle == "rb").Role);
        Assert.Equal(TapeRole.DrivenEarlier, run.Tapes.Single(tape => tape.Vehicle == "ra").Role);
    }

    [Fact]
    public void Only_the_last_launches_are_remembered()
    {
        var installation = new TestInstallation(new InMemoryFileSystem());
        var tapes = installation.Tapes;
        for (int i = 0; i < 35; i++)
        {
            installation.Clock.Now = Launched.AddMinutes(i);
            tapes.RecordLaunch($"s{i}.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>());
        }

        var noted = TapeLaunch.ParseAll(installation.Files.ReadAllText(
            TestInstallation.At("physicslog", "starter-launch.txt"), System.Text.Encoding.UTF8));

        Assert.Equal(30, noted.Count);
        Assert.Equal("s5.scn", noted[0].Scenery);
        Assert.Equal("s34.scn", noted[^1].Scenery);
    }

    [Fact]
    public void The_vehicle_the_commands_go_to_is_the_one_the_player_is_in()
    {
        var player = PlayerVehicles.None.After(
            "[   21.764 ]\t\tTrying to enter player train, \"lok-a\"\r\n" +
            "[   25.609 ]\t\tlok-a received command: [aidriverdisable]\r\n" +
            "[  255.539 ]\t\tKey pressed: [F5]\r\n" +
            "[  255.569 ]\t\tlok b received command: [aidriverdisable]\r\n" +
            "lok-c received command: [cabchangebackward]\n");

        Assert.Equal("lok-c", player.Current);
        Assert.True(player.Used("LOK-A"));
        Assert.True(player.Used("lok b"));
        Assert.False(player.Used("lok-d"));
    }

    [Fact]
    public void Going_back_to_a_vehicle_makes_it_the_current_one_again()
    {
        var player = PlayerVehicles.None
            .After("a received command: [x]\nb received command: [x]\n")
            .After("A received command: [x]\n");

        Assert.Equal("A", player.Current);
        Assert.True(player.Used("b"));
    }

    [Fact]
    public void After_a_change_of_vehicle_the_new_one_is_driven_and_the_first_was_driven_earlier()
    {
        var launch = new TapeLaunch(Launched, "td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(),
                                    "ra", new[] { "ra", "s", "rb" });
        var player = PlayerVehicles.None.After("ra received command: [x]\nrb received command: [x]\n");

        Assert.Equal(TapeRole.Driven, launch.RoleOf("RB", player));
        Assert.Equal(TapeRole.DrivenEarlier, launch.RoleOf("ra", player));
        Assert.Equal(TapeRole.OwnConsist, launch.RoleOf("s", player));
        Assert.Equal(TapeRole.Other, launch.RoleOf("obcy", player));
    }

    [Fact]
    public void Before_the_first_command_the_vehicle_of_the_launch_is_driven()
    {
        var launch = new TapeLaunch(Launched, "td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "ra", new[] { "ra", "rb" });

        Assert.Equal(TapeRole.Driven, launch.RoleOf("ra", PlayerVehicles.None));
        Assert.Equal(TapeRole.OwnConsist, launch.RoleOf("rb", PlayerVehicles.None));
    }

    private static (HaslerTapes Tapes, TestInstallation Installation) TapesOfTrain(string log)
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("physicslog", "ra.dat"), "t")
            .WithFile(TestInstallation.At("physicslog", "rb.dat"), "t")
            .WithFile(TestInstallation.At("physicslog", "obcy.dat"), "t")
            .WithFile(TestInstallation.At("log.txt"), log));
        installation.Clock.Now = Launched;
        var tapes = installation.Tapes;
        tapes.RecordLaunch("td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "ra", new[] { "ra", "rb" }, "Test", "MPJ1");
        return (tapes, installation);
    }

    [Fact]
    public void The_tape_of_the_vehicle_changed_to_comes_first_and_keeps_the_train()
    {
        var (tapes, _) = TapesOfTrain("ra received command: [x]\nrb received command: [x]\n");

        Assert.Equal(new[] { "rb", "ra", "obcy" }, tapes.List().Select(tape => tape.Vehicle));
        Assert.Equal(new[] { TapeRole.Driven, TapeRole.DrivenEarlier, TapeRole.Other }, tapes.List().Select(tape => tape.Role));
        Assert.Equal("rb", tapes.DrivenVehicle);
        Assert.Equal("MPJ1", tapes.ContextOf(tapes.List().First()).Train);
    }

    [Fact]
    public void A_vehicle_of_another_train_changed_to_is_driven_without_the_number_of_the_first()
    {
        var (tapes, _) = TapesOfTrain("obcy received command: [x]\n");

        var driven = tapes.List().First();

        Assert.Equal("obcy", driven.Vehicle);
        Assert.Null(tapes.ContextOf(driven).Train);
    }

    [Fact]
    public void A_change_of_vehicle_during_the_run_is_seen_at_the_next_look()
    {
        const string start = "ra received command: [x]\n";
        var (tapes, installation) = TapesOfTrain(start);
        Assert.Equal("ra", tapes.DrivenVehicle);

        installation.Files.WithFile(TestInstallation.At("log.txt"), start + "rb received com");
        Assert.Equal("ra", tapes.DrivenVehicle);

        installation.Files.WithFile(TestInstallation.At("log.txt"), start + "rb received command: [x]\n");
        Assert.Equal("rb", tapes.DrivenVehicle);
    }

    [Fact]
    public void A_log_begun_anew_forgets_the_vehicles_of_the_old_one()
    {
        var (tapes, installation) = TapesOfTrain("ra received command: [x]\nrb received command: [x]\n");
        Assert.Equal("rb", tapes.DrivenVehicle);

        installation.Files.WithFile(TestInstallation.At("log.txt"), "obcy received command: [x]\n");

        Assert.Equal("obcy", tapes.DrivenVehicle);
        Assert.Equal(TapeRole.OwnConsist, tapes.List().Single(tape => tape.Vehicle == "rb").Role);
    }

    [Fact]
    public void A_log_older_than_the_launch_says_nothing_of_it()
    {
        var (tapes, installation) = TapesOfTrain("rb received command: [x]\n");
        installation.Clock.Now = installation.Files.LastWriteTimeUtc.AddMinutes(10);
        tapes.RecordLaunch("td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "ra", new[] { "ra", "rb" });

        Assert.Equal("ra", tapes.DrivenVehicle);
    }

    [Fact]
    public void A_tape_older_than_the_last_launch_has_no_part_in_it()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("physicslog", "c-lok.dat"), "t"));
        installation.Clock.Now = installation.Files.LastWriteTimeUtc.AddDays(1);
        var tapes = installation.Tapes;

        tapes.RecordLaunch("td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "c-lok");

        Assert.Equal(TapeRole.Other, tapes.List().Single().Role);
    }

    [Fact]
    public void A_log_written_before_the_launch_is_not_taken_for_one_of_it()
    {
        var launch = new TapeLaunch(Launched, "td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>());

        Assert.True(launch.Covers(Launched.AddMinutes(3)));
        Assert.False(launch.Covers(Launched.AddHours(-1)));
    }

    [Theory]
    [InlineData("pkp/ep07_v1", "ep07-424", RecorderKind.Hasler)]
    [InlineData("pkp/en57al_v1", "en57al-1234", RecorderKind.Electronic)]
    [InlineData("pkp/x4e_v1", "x4e-659", RecorderKind.Electronic)]
    [InlineData("pkp/et22_v3", "et22-1100", RecorderKind.Hasler)]
    public void The_recorder_follows_the_vehicle_behind_the_name(string folder, string type, RecorderKind kind) =>
        Assert.Equal(kind, Recorders.Guess(new TapeVehicle("poc1_lok", folder, type), "poc1_lok"));

    [Fact]
    public void Without_a_launch_the_tape_name_is_all_there_is_to_go_by()
    {
        Assert.Equal(RecorderKind.Electronic, Recorders.Guess(null, "ED250-001"));
        Assert.Equal(RecorderKind.Hasler, Recorders.Guess(null, "eu07-193"));
    }

    [Fact]
    public void A_tape_of_the_last_launch_gets_its_clock_and_recorder()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("physicslog", "ED250-001.dat"), "t"));
        installation.Clock.Now = Launched;
        var tapes = installation.Tapes;

        tapes.RecordLaunch("lk4.scn", new TimeSpan(14, 5, 0),
                           new[] { new TapeVehicle("ED250-001", "pkp/ed250_v1", "ed250-001") });
        var context = tapes.ContextOf(tapes.List().Single());

        Assert.Equal(new TimeSpan(14, 5, 0), context.StartClock);
        Assert.Equal(RecorderKind.Electronic, context.Recorder);
    }

    [Fact]
    public void A_tape_older_than_the_last_launch_keeps_its_hour_unknown()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("physicslog", "EU07-424.dat"), "t"));
        installation.Clock.Now = installation.Files.LastWriteTimeUtc.AddDays(1);
        var tapes = installation.Tapes;

        tapes.RecordLaunch("td.scn", new TimeSpan(8, 0, 0), Array.Empty<TapeVehicle>());

        Assert.Null(tapes.ContextOf(tapes.List().Single()).StartClock);
    }
}
