using StarterNG.Domain.Tapes;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests.BlackBox;

public class BlackBoxListingTests
{
    private static readonly DateTime Launched = new(2025, 12, 31, 23, 0, 0, DateTimeKind.Utc);

    private static string Log(string name) => TestInstallation.At("physicslog", name);

    /// <summary>A black box's name: the simulator stamps it with the local time it started at.</summary>
    private static string Box(string scenery, DateTime startedUtc) =>
        $"{scenery}_{startedUtc.ToLocalTime():yyyy-MM-dd_HH-mm-ss}.blackbox";

    [Fact]
    public void Each_run_keeps_its_own_black_box_next_to_its_tapes()
    {
        string first = Box("td", Launched.AddSeconds(5)), second = Box("td", Launched.AddHours(1).AddSeconds(5));
        var files = new InMemoryFileSystem()
            .WithFile(Log("ra.dat"), "t")
            .WithFile(Log(first), "b")
            .WithFile(Log(second), "b");
        var installation = new TestInstallation(files);
        var tapes = installation.Tapes;
        installation.Clock.Now = Launched;
        tapes.RecordLaunch("td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "ra", new[] { "ra" }, "A");
        installation.Clock.Now = Launched.AddHours(1);
        tapes.RecordLaunch("td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "ra", new[] { "ra" }, "B");
        files.WrittenAt(Log(first), Launched.AddMinutes(30))
             .WrittenAt(Log(second), Launched.AddMinutes(90))
             .WrittenAt(Log("ra.dat"), Launched.AddMinutes(90));

        var sessions = tapes.Sessions();

        Assert.Equal(new[] { "B", "A" }, sessions.Select(session => session.Launch!.SceneryName));
        Assert.EndsWith(second, Assert.Single(sessions[0].BlackBoxes).Path);
        Assert.Single(sessions[0].Tapes);
        // The tape of the first run was written over by the second; its black box was not.
        Assert.EndsWith(first, Assert.Single(sessions[1].BlackBoxes).Path);
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
        string box = Box("td", Launched.AddSeconds(20));
        files.WithFile(Log(box), "b")
             .WrittenAt(Log(box), Launched.AddMinutes(5));

        installation.SimulatorRunning = true;
        var running = Assert.Single(tapes.Sessions());

        Assert.True(running.Running);
        Assert.Empty(running.Tapes);
        Assert.Single(running.BlackBoxes);
    }

    [Fact]
    public void A_black_box_of_another_scenery_written_after_a_launch_is_not_taken_for_its_run()
    {
        // Copied in, or a run started without the starter: written after the last launch, but of another scenery.
        var files = new InMemoryFileSystem();
        var installation = new TestInstallation(files);
        installation.Clock.Now = Launched;
        installation.Tapes.RecordLaunch("$baltyk.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "ra", new[] { "ra" }, "Bałtyk");
        string own = Box("baltyk", Launched.AddSeconds(10)), other = Box("l053_poludnie", Launched.AddHours(3));
        files.WithFile(Log(own), "b").WrittenAt(Log(own), Launched.AddMinutes(40))
             .WithFile(Log(other), "b").WrittenAt(Log(other), Launched.AddHours(4));

        var sessions = installation.Tapes.Sessions();

        Assert.Equal(2, sessions.Count);
        var loose = Assert.Single(sessions, session => session.Launch is null);
        Assert.EndsWith(other, Assert.Single(loose.BlackBoxes).Path);
        var launched = Assert.Single(sessions, session => session.Launch is not null);
        Assert.EndsWith(own, Assert.Single(launched.BlackBoxes).Path);
    }

    [Fact]
    public void A_tape_written_after_another_run_began_is_not_taken_for_the_launch_before()
    {
        // The starter launched Bałtyk; later a run of another scenery, not launched from it, wrote its tape and black box.
        var files = new InMemoryFileSystem();
        var installation = new TestInstallation(files);
        installation.Clock.Now = Launched;
        installation.Tapes.RecordLaunch("baltyk.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "ep07", new[] { "ep07" }, "Bałtyk");
        string box = Box("td", Launched.AddHours(5));
        files.WithFile(Log("ep07.dat"), "t").WrittenAt(Log("ep07.dat"), Launched.AddMinutes(50))
             .WithFile(Log("bb-su45.dat"), "t").WrittenAt(Log("bb-su45.dat"), Launched.AddHours(5).AddMinutes(8))
             .WithFile(Log(box), "b").WrittenAt(Log(box), Launched.AddHours(5).AddMinutes(8));

        var sessions = installation.Tapes.Sessions();

        var baltyk = Assert.Single(sessions, session => session.Launch is not null);
        Assert.Equal("ep07", Assert.Single(baltyk.Tapes).Vehicle);
        Assert.Empty(baltyk.BlackBoxes);
        var other = Assert.Single(sessions, session => session.Launch is null);
        Assert.Equal("bb-su45", Assert.Single(other.Tapes).Vehicle);
        Assert.Single(other.BlackBoxes);
        Assert.Null(installation.Tapes.ContextOf(other.Tapes[0]).StartClock);
    }

    [Fact]
    public void A_black_box_started_before_a_launch_is_not_taken_for_it()
    {
        // Started earlier, and only finished writing after the starter launched the same scenery again.
        var files = new InMemoryFileSystem();
        var installation = new TestInstallation(files);
        installation.Clock.Now = Launched;
        installation.Tapes.RecordLaunch("td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "ra");
        string early = Box("td", Launched.AddHours(-2));
        files.WithFile(Log(early), "b").WrittenAt(Log(early), Launched.AddMinutes(1));

        var session = Assert.Single(installation.Tapes.Sessions());

        Assert.Null(session.Launch);
        Assert.Single(session.BlackBoxes);
    }

    [Theory]
    [InlineData("$td.scn", "td")]
    [InlineData("l053_poludnie.scn", "l053_poludnie")]
    [InlineData("Bałtyk zima.scn", "Ba_tyk_zima")]
    public void The_scenery_is_named_as_the_simulator_names_the_black_box(string scenery, string stem) =>
        Assert.Equal(stem, StarterNG.Application.HaslerTapes.SceneryStem(scenery));

    [Theory]
    [InlineData("Ba_tyk_zima", true)]   // Windows: 'ł' is one byte of its code page
    [InlineData("Ba__tyk_zima", true)]  // Linux: two bytes of UTF-8
    [InlineData("BA__TYK_ZIMA", true)]
    [InlineData("Baltyk_zima", false)]
    public void A_letter_outside_ascii_matches_however_many_bytes_it_was(string named, bool same) =>
        Assert.Equal(same, StarterNG.Application.HaslerTapes.SameStem(
            StarterNG.Application.HaslerTapes.SceneryStem("Bałtyk zima.scn"), named));
}
