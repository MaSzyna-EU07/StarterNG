using StarterNG.Application;
using StarterNG.Infrastructure.Adapters;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class SimulatorExitTests
{
    private static readonly TimeSpan Session = TimeSpan.FromMinutes(20);

    [Fact]
    public void A_session_ended_cleanly_is_no_failure()
    {
        Assert.False(new SimulatorExit(0, Session, []).Failed);
    }

    [Fact]
    public void An_error_code_is_a_failure()
    {
        Assert.True(new SimulatorExit(1, Session, []).Failed);
    }

    [Fact]
    public void A_window_that_only_flashed_is_a_failure_even_with_a_clean_code()
    {
        var exit = new SimulatorExit(0, TimeSpan.FromSeconds(0.2), []);

        Assert.True(exit.Failed);
        Assert.True(exit.Quick);
    }

    [Theory]
    [InlineData(139, "139 (SIGSEGV)")]
    [InlineData(134, "134 (SIGABRT)")]
    [InlineData(-1073741819, "0xC0000005 (access violation)")]
    [InlineData(3, "3")]
    public void The_code_says_what_killed_it(int code, string described)
    {
        Assert.Equal(described, new SimulatorExit(code, Session, []).DescribeCode());
    }

    [Fact]
    public void Colour_codes_and_blank_lines_are_dropped_from_the_errors()
    {
        var exit = new SimulatorExit(1, Session, ["\u001b[31mBad file: texture\u001b[0m", "", "   "]);

        Assert.Equal(["Bad file: texture"], exit.CleanErrors);
    }

    [Fact]
    public async Task A_real_process_leaves_its_exit_code_and_error_stream()
    {
        var launcher = new SystemProcessLauncher(new TestInstallation().Log);
        var (shell, script) = OperatingSystem.IsWindows()
            ? ("cmd", new[] { "/c", "echo boom 1>&2 & exit 3" })
            : ("/bin/sh", new[] { "-c", "echo boom >&2; exit 3" });

        var process = launcher.Start(shell, script, null, captureErrors: true, out string? error);
        Assert.Null(error);
        await process!.WaitForExitAsync();

        Assert.Equal(3, process.ExitCode);
        Assert.Equal("boom", Assert.Single(process.ErrorTail).Trim());
    }
}
