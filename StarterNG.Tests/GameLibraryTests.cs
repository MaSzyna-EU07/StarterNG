using StarterNG.Application;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class GameLibraryTests
{
    private static TestInstallation Complete() =>
        new(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("dynamic", "pkp", "ep07", "textures.txt"), "ep07-135.mat=ep07.e3d,ep07")
            .WithFile(TestInstallation.At("scenery", "kato.scn"), "//$n Katowice")
            .WithFile(TestInstallation.At("textures", "mini", "ep07.bmp"), "bitmap"));

    [Fact]
    public void Loading_reads_both_the_rolling_stock_and_the_scenarios()
    {
        var library = Complete().Library;

        library.Load();

        Assert.True(library.Loaded);
        Assert.Single(library.Vehicles.Textures);
        Assert.Equal("Katowice", Assert.Single(library.Sceneries).Name);
    }

    [Fact]
    public void Loading_reports_progress_through_to_done()
    {
        var phases = new List<LoadPhase>();
        var progress = new Progress<LoadStatus>(status => { lock (phases) phases.Add(status.Phase); });

        Complete().Library.Load(progress);

        SpinWait.SpinUntil(() => { lock (phases) return phases.Contains(LoadPhase.Done); }, TimeSpan.FromSeconds(2));
        lock (phases)
        {
            Assert.Contains(LoadPhase.Vehicles, phases);
            Assert.Contains(LoadPhase.Done, phases);
        }
    }

    [Fact]
    public void Loading_twice_does_not_duplicate_the_scenarios()
    {
        var library = Complete().Library;

        library.Load();
        library.Load();

        Assert.Single(library.Sceneries);
    }

    [Fact]
    public void Reloading_picks_up_a_scenery_added_after_startup()
    {
        var installation = Complete();
        var library = installation.Library;
        library.Load();

        installation.Files.WithFile(TestInstallation.At("scenery", "gdansk.scn"), "//$n Gdańsk");
        library.ReloadSceneries();

        Assert.Equal(2, library.Sceneries.Count);
    }

    [Fact]
    public void An_installation_with_no_rolling_stock_is_logged()
    {
        var installation = new TestInstallation();

        installation.Library.Load();

        Assert.Contains("textures.txt", installation.Log.Text);
    }
}
