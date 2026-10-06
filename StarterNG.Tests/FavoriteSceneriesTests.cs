using StarterNG.Application;
using StarterNG.Domain.Sceneries;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class FavoriteSceneriesTests
{
    private const string FavoritesPath = "/home/user/.config/MaSzyna/favorites.txt";

    [Fact]
    public void A_starred_scenery_stays_starred_after_a_restart()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithLegacyFile(TestInstallation.At("scenery", "td.scn"), "//$n Testowa"));
        var scenery = Assert.Single(installation.Sceneries.LoadAll());

        new FavoriteSceneries(installation.Files, installation.Log, FavoritesPath).Toggle(scenery);
        var restarted = new FavoriteSceneries(installation.Files, installation.Log, FavoritesPath);

        Assert.True(restarted.Contains(scenery));
        Assert.Equal("td.scn", installation.Files.ReadAllText(FavoritesPath));
    }

    [Fact]
    public void Starring_again_unstars()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithLegacyFile(TestInstallation.At("scenery", "td.scn"), "//$n Testowa"));
        var scenery = Assert.Single(installation.Sceneries.LoadAll());
        var favorites = new FavoriteSceneries(installation.Files, installation.Log, FavoritesPath);

        favorites.Toggle(scenery);
        favorites.Toggle(scenery);

        Assert.False(favorites.Contains(scenery));
    }
}
