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

    private static string Written(string path)
    {
        // The writer holds it open for as long as the session lasts.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void The_first_entry_of_a_session_keeps_the_log_of_the_one_before()
    {
        string root = Directory.CreateTempSubdirectory("starterng-log-").FullName;
        try
        {
            var paths = new GamePaths(root);
            Directory.CreateDirectory(paths.StarterConfig);
            File.WriteAllText(paths.DiagnosticsLog, "the crash\n");
            var log = new FileDiagnosticsLog(new PhysicalFileSystem(), new FixedClock(new DateTime(2026, 3, 7)), paths);

            log.Log("after the restart");
            log.Log("and on");

            Assert.Equal("the crash\n", Written(paths.DiagnosticsLog + ".1"));
            string now = Written(paths.DiagnosticsLog);
            Assert.DoesNotContain("the crash", now);
            Assert.Contains("after the restart", now);
            Assert.Contains("and on", now);
            log.Clear();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Only_the_session_before_is_kept()
    {
        string root = Directory.CreateTempSubdirectory("starterng-log-").FullName;
        try
        {
            var paths = new GamePaths(root);
            Directory.CreateDirectory(paths.StarterConfig);
            File.WriteAllText(paths.DiagnosticsLog, "the last one\n");
            File.WriteAllText(paths.DiagnosticsLog + ".1", "the one before it\n");
            var log = new FileDiagnosticsLog(new PhysicalFileSystem(), new FixedClock(new DateTime(2026, 3, 7)), paths);

            log.Log("now");

            Assert.Equal("the last one\n", Written(paths.DiagnosticsLog + ".1"));
            log.Clear();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Clearing_the_log_does_not_touch_the_session_kept()
    {
        var log = Build(out var files);
        string path = new GamePaths("game").DiagnosticsLog;
        files.WithFile(path, "the crash\n");

        log.Log("after the restart");
        log.Clear();
        log.Log("cleared");

        Assert.Equal("the crash\n", files.ReadAllText(path + ".1"));
    }

    [Fact]
    public void A_log_that_cannot_be_copied_is_added_to_and_not_wiped()
    {
        string root = Directory.CreateTempSubdirectory("starterng-log-").FullName;
        try
        {
            var paths = new GamePaths(root);
            Directory.CreateDirectory(paths.StarterConfig);
            File.WriteAllText(paths.DiagnosticsLog, "the crash\n");
            // A folder where the copy would go: nothing can be written in its place.
            Directory.CreateDirectory(paths.DiagnosticsLog + ".1");
            var log = new FileDiagnosticsLog(new PhysicalFileSystem(), new FixedClock(new DateTime(2026, 3, 7)), paths);

            log.Log("after the restart");

            string now = Written(paths.DiagnosticsLog);
            Assert.Contains("the crash", now);
            Assert.Contains("after the restart", now);
            log.Clear();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_folder_it_cannot_write_in_does_not_stop_the_program()
    {
        string root = Directory.CreateTempSubdirectory("starterng-log-").FullName;
        try
        {
            var paths = new GamePaths(root);
            // A file where the log's folder should be, as unwritable as read-only gets
            // on every platform and for every user.
            File.WriteAllText(paths.StarterConfig, "");
            var log = new FileDiagnosticsLog(new PhysicalFileSystem(), new FixedClock(new DateTime(2026, 3, 7)), paths);

            log.Log("first");
            log.Log("second");
            log.Flush();
            log.Clear();
            log.Log("third");

            Assert.Contains("third", log.Text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
