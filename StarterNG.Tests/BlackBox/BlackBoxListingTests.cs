using StarterNG.Domain.Tapes;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests.BlackBox;

public class BlackBoxListingTests
{
    private static readonly DateTime Launched = new(2025, 12, 31, 23, 0, 0, DateTimeKind.Utc);

    private static string Log(string name) => TestInstallation.At("physicslog", name);

    [Fact]
    public void Each_run_keeps_its_own_black_box_next_to_its_tapes()
    {
        var files = new InMemoryFileSystem()
            .WithFile(Log("ra.dat"), "t")
            .WithFile(Log("td_2025-12-31_23-00-05.blackbox"), "b")
            .WithFile(Log("td_2026-01-01_00-00-05.blackbox"), "b");
        var installation = new TestInstallation(files);
        var tapes = installation.Tapes;
        installation.Clock.Now = Launched;
        tapes.RecordLaunch("td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "ra", new[] { "ra" }, "A");
        installation.Clock.Now = Launched.AddHours(1);
        tapes.RecordLaunch("td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "ra", new[] { "ra" }, "B");
        files.WrittenAt(Log("td_2025-12-31_23-00-05.blackbox"), Launched.AddMinutes(30))
             .WrittenAt(Log("td_2026-01-01_00-00-05.blackbox"), Launched.AddMinutes(90))
             .WrittenAt(Log("ra.dat"), Launched.AddMinutes(90));

        var sessions = tapes.Sessions();

        Assert.Equal(new[] { "B", "A" }, sessions.Select(session => session.Launch!.SceneryName));
        Assert.EndsWith("00-00-05.blackbox", Assert.Single(sessions[0].BlackBoxes).Path);
        Assert.Single(sessions[0].Tapes);
        // The tape of the first run was written over by the second; its black box was not.
        Assert.EndsWith("23-00-05.blackbox", Assert.Single(sessions[1].BlackBoxes).Path);
        Assert.Empty(sessions[1].Tapes);
        Assert.Equal(Launched.AddMinutes(30), sessions[1].LastWrittenUtc);
    }

    [Fact]
    public void A_black_box_is_not_taken_for_a_tape()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(Log("ra.dat"), "t")
            .WithFile(Log("td_2026-03-01_12-00-00.blackbox"), "b"));

        var session = Assert.Single(installation.Tapes.Sessions());

        Assert.Equal("ra", Assert.Single(session.Tapes).Vehicle);
        Assert.Single(session.BlackBoxes);
        Assert.Equal("ra", Assert.Single(installation.Tapes.List()).Vehicle);
    }

    [Fact]
    public void Black_boxes_with_no_launch_noted_go_with_the_tapes_written_about_the_same_time()
    {
        var early = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(Log("a.dat"), "t")
            .WithFile(Log("td_2026-03-01_11-00-00.blackbox"), "b")
            .WithFile(Log("td_2026-03-01_12-30-00.blackbox"), "b")
            .WrittenAt(Log("a.dat"), early)
            .WrittenAt(Log("td_2026-03-01_11-00-00.blackbox"), early.AddSeconds(2))
            .WrittenAt(Log("td_2026-03-01_12-30-00.blackbox"), early.AddHours(1)));

        var sessions = installation.Tapes.Sessions();

        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, session => Assert.Null(session.Launch));
        // Newest first: the black box on its own, then the run with the tape.
        Assert.Empty(sessions[0].Tapes);
        Assert.EndsWith("12-30-00.blackbox", Assert.Single(sessions[0].BlackBoxes).Path);
        Assert.Single(sessions[1].Tapes);
        Assert.EndsWith("11-00-00.blackbox", Assert.Single(sessions[1].BlackBoxes).Path);
        Assert.Equal(early, sessions[1].StartedUtc);
    }

    [Fact]
    public void The_black_box_of_a_running_session_is_listed_while_it_is_written()
    {
        var files = new InMemoryFileSystem();
        var installation = new TestInstallation(files);
        installation.Clock.Now = Launched;
        var tapes = installation.Tapes;
        tapes.RecordLaunch("td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "ra");
        files.WithFile(Log("td_2025-12-31_23-00-20.blackbox"), "b")
             .WrittenAt(Log("td_2025-12-31_23-00-20.blackbox"), Launched.AddMinutes(5));

        installation.SimulatorRunning = true;
        var running = Assert.Single(tapes.Sessions());

        Assert.True(running.Running);
        Assert.Empty(running.Tapes);
        Assert.Single(running.BlackBoxes);
    }
}
