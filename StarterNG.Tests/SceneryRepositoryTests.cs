using System.Text;
using StarterNG.Infrastructure.Adapters;
using StarterNG.Infrastructure.Sceneries;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class SceneryRepositoryTests
{
    private static SceneryRepository Build(InMemoryFileSystem files, out FileDiagnosticsLog log)
    {
        var paths = new GamePaths("game");
        var clock = new FixedClock(new DateTime(2026, 1, 1));
        log = new FileDiagnosticsLog(files, clock, paths);
        return new SceneryRepository(files, paths, log, new SceneryParser(clock, new StubRandom()));
    }

    [Fact]
    public void Every_scn_in_the_scenery_folder_is_loaded()
    {
        var files = new InMemoryFileSystem()
            .WithFile(Path.Combine("game", "scenery", "kato.scn"), "//$n Katowice")
            .WithFile(Path.Combine("game", "scenery", "gdansk.scn"), "//$n Gdańsk");

        var loaded = Build(files, out _).LoadAll();

        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, scenery => scenery.Name == "Katowice");
    }

    [Fact]
    public void Authoring_scratch_files_and_other_extensions_are_skipped()
    {
        var files = new InMemoryFileSystem()
            .WithFile(Path.Combine("game", "scenery", "$wip.scn"), "//$n Robocza")
            .WithFile(Path.Combine("game", "scenery", "kato.inc"), "//$n Include")
            .WithFile(Path.Combine("game", "scenery", "kato.scn"), "//$n Katowice");

        var loaded = Build(files, out _).LoadAll();

        Assert.Equal("Katowice", Assert.Single(loaded).Name);
    }

    [Fact]
    public void A_companion_sbt_is_noticed_when_the_scenery_is_loaded()
    {
        var files = new InMemoryFileSystem()
            .WithFile(Path.Combine("game", "scenery", "kato.scn"), "//$n Katowice")
            .WithFile(Path.Combine("game", "scenery", "kato.sbt"), "timetable");

        var scenery = Assert.Single(Build(files, out _).LoadAll());

        Assert.True(scenery.HasCompanionTimetable);
    }

    [Fact]
    public void A_missing_scenery_loads_as_null_rather_than_throwing()
    {
        Assert.Null(Build(new InMemoryFileSystem(), out _).Load(Path.Combine("game", "scenery", "gone.scn")));
    }

    [Fact]
    public void Progress_is_reported_once_per_file()
    {
        var files = new InMemoryFileSystem()
            .WithFile(Path.Combine("game", "scenery", "a.scn"), "//$n A")
            .WithFile(Path.Combine("game", "scenery", "b.scn"), "//$n B");
        var steps = new List<int>();

        Build(files, out _).LoadAll(new Progress<StarterNG.Application.Abstractions.SceneryLoadProgress>(
            step => { lock (steps) steps.Add(step.Loaded); }));

        // Progress<T> posts asynchronously; give it a moment to drain.
        SpinWait.SpinUntil(() => { lock (steps) return steps.Count == 2; }, TimeSpan.FromSeconds(2));
        lock (steps)
            Assert.Equal(new[] { 1, 2 }, steps.OrderBy(value => value));
    }

    [Fact]
    public void The_preview_image_is_found_next_to_the_scenery()
    {
        var files = new InMemoryFileSystem()
            .WithFile(Path.Combine("game", "scenery", "kato.scn"), "//$i kato.jpg")
            .WithFile(Path.Combine("game", "scenery", "images", "kato.jpg"), "binary");
        var scenery = Assert.Single(Build(files, out _).LoadAll());

        string? image = new SceneryImageLocator(files).Resolve(scenery);

        Assert.Equal(Path.Combine("game", "scenery", "images", "kato.jpg"), image);
    }

    [Fact]
    public void A_scenery_without_an_image_directive_resolves_to_nothing()
    {
        var files = new InMemoryFileSystem()
            .WithFile(Path.Combine("game", "scenery", "kato.scn"), "//$n Katowice");
        var scenery = Assert.Single(Build(files, out _).LoadAll());

        Assert.Null(new SceneryImageLocator(files).Resolve(scenery));
    }
}
