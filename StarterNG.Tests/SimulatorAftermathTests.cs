using StarterNG.Application;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class SimulatorAftermathTests
{
    private static readonly string Build =
        Path.Combine(Path.GetTempPath(), "maszyna", "cmake-build-diag", "bin", "eu07_2026-10-04_fc195880");

    [Fact]
    public void Minidumps_of_this_run_are_found_in_the_installation_and_under_crashpad()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithExecutable(Build)
            .WithFile(TestInstallation.At("crash_2026-10-04_13-03-12.dmp"), "MDMP")
            .WithFile(TestInstallation.At("crashdumps", "reports", "1a2b.dmp"), "MDMP"));
        installation.Files.LastWriteTimeUtc = new DateTime(2026, 10, 4, 11, 3, 12, DateTimeKind.Utc);
        var aftermath = new SimulatorAftermath(installation.Files, installation.Paths, installation.Log);

        var thisRun = aftermath.CrashDumpsSince(new DateTime(2026, 10, 4, 11, 3, 0, DateTimeKind.Utc), Build);
        var later = aftermath.CrashDumpsSince(new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc), Build);

        Assert.Equal(2, thisRun.Count);
        Assert.Contains(thisRun, dump => dump.EndsWith("crash_2026-10-04_13-03-12.dmp"));
        Assert.Empty(later);
    }

    [Fact]
    public void The_end_of_the_simulator_log_is_read_for_a_silent_crash()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("log.txt"), "first\r\nsecond\r\n\r\nthird\r\n"));

        var tail = new SimulatorAftermath(installation.Files, installation.Paths, installation.Log).SimulatorLogTail(2);

        Assert.Equal(new[] { "second", "third" }, tail);
    }
}
