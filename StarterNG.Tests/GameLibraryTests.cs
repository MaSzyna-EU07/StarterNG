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

    [Fact]
    public void A_folder_of_the_rolling_stock_that_cannot_be_listed_costs_only_itself()
    {
        var files = new InMemoryFileSystem()
            .WithFile(TestInstallation.At("dynamic", "aaa", "ep09", "textures.txt"), "ep09-1.mat=ep09.e3d,ep09")
            .WithFile(TestInstallation.At("dynamic", "aaa", "ep09", "ep09.fiz"), "Param: M=2")
            .WithFile(TestInstallation.At("dynamic", "pkp", "ep07", "textures.txt"), "ep07-135.mat=ep07.e3d,ep07")
            .WithFile(TestInstallation.At("dynamic", "pkp", "ep07", "ep07.fiz"), "Param: M=1")
            .WithFile(TestInstallation.At("scenery", "kato.scn"), "//$n Katowice")
            .DenyListing(TestInstallation.At("dynamic", "aaa"));
        var installation = new TestInstallation(files);

        installation.Library.Load();

        Assert.True(installation.Library.Loaded);
        Assert.Equal("ep07-135", Assert.Single(installation.Library.Vehicles.Textures).Skinfile);
        Assert.Equal("Katowice", Assert.Single(installation.Library.Sceneries).Name);
        Assert.Contains("aaa", installation.Log.Text);
        Assert.Equal(1, installation.Physics.For("pkp/ep07/", "ep07")!.Mass);
    }

    [Fact]
    public void The_whole_rolling_stock_failing_still_leaves_the_sceneries()
    {
        var installation = Complete();
        var library = new GameLibrary(new FailingVehicles(), installation.Sceneries, installation.MiniTextures,
                                      installation.Physics, installation.Log);

        library.Load();

        Assert.True(library.Loaded);
        Assert.Equal("Katowice", Assert.Single(library.Sceneries).Name);
        Assert.Contains("no disk today", installation.Log.Text);
    }

    private sealed class FailingVehicles : StarterNG.Application.Abstractions.IVehicleRepository
    {
        public int Load(StarterNG.Domain.Vehicles.VehicleCatalog catalog) =>
            throw new IOException("no disk today");
    }
}
