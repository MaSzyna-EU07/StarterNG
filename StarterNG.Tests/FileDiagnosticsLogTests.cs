using StarterNG.Infrastructure.Adapters;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class FileDiagnosticsLogTests
{
    private static FileDiagnosticsLog Build(out InMemoryFileSystem files)
    {
        files = new InMemoryFileSystem();
        return new FileDiagnosticsLog(files, new FixedClock(new DateTime(2026, 3, 7, 14, 5, 0)),
                                      new GamePaths("game"));
    }

    [Fact]
    public void Log_timestamps_each_line_from_the_clock()
    {
        var log = Build(out _);

        log.Log("scenery loaded");

        Assert.Equal("2026-03-07 14:05:00  scenery loaded", log.Text);
    }

    [Fact]
    public void Exceptions_are_logged_with_their_context_and_type()
    {
        var log = Build(out _);

        log.Log("scenery/foo.scn", new InvalidOperationException("bad node"));

        Assert.Contains("scenery/foo.scn: InvalidOperationException: bad node", log.Text);
    }

    [Fact]
    public void Clear_drops_everything_logged_so_far()
    {
        var log = Build(out _);
        log.Log("first");

        log.Clear();

        Assert.Equal(string.Empty, log.Text);
    }
}
