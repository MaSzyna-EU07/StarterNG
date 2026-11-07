using StarterNG.Application.Abstractions;
using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Tests.Fakes;

/// <summary>The diagnostics log for a test that only wants one to write to or read back.</summary>
public static class TestLog
{
    /// <param name="files">Where the log file goes; a disk of its own when left out.</param>
    public static FileDiagnosticsLog Over(IFileSystem? files = null) =>
        new(files ?? new InMemoryFileSystem(), new FixedClock(new DateTime(2026, 10, 13)), new GamePaths("game"));
}
