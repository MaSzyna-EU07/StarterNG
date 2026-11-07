using StarterNG.Application.Abstractions;
using StarterNG.Infrastructure.Adapters;
using StarterNG.Infrastructure.Sceneries;

namespace StarterNG.Tests.Fakes;

/// <summary>The scenery reader over a fake clock and dice, for tests of what it makes of a text.</summary>
public static class TestScenery
{
    /// <param name="now">The hour a scenery that names none starts at.</param>
    public static SceneryParser Parser(DateTime? now = null) =>
        new(new FixedClock(now ?? new DateTime(2026, 1, 1, 12, 0, 0)), new StubRandom());

    public static SceneryRepository Repository(IFileSystem files) =>
        new(files, new GamePaths("game"), TestLog.Over(files), Parser());
}
