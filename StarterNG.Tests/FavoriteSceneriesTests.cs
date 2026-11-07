using StarterNG.Application;
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

    [Fact]
    public void A_file_that_could_not_be_read_goes_aside_before_a_star_replaces_it()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithLegacyFile(TestInstallation.At("scenery", "td.scn"), "//$n Testowa")
            .WithFile(FavoritesPath, "one.scn\ntwo.scn")
            .FailReading(FavoritesPath));
        var scenery = Assert.Single(installation.Sceneries.LoadAll());
        var favorites = new FavoriteSceneries(installation.Files, installation.Log, FavoritesPath);

        installation.Files.AllowReading(FavoritesPath);
        favorites.Toggle(scenery);

        Assert.Equal("one.scn\ntwo.scn", installation.Files.ReadAllText(FavoritesPath + ".unreadable"));
        Assert.Equal("td.scn", installation.Files.ReadAllText(FavoritesPath));
        Assert.Contains(FavoritesPath + ".unreadable", installation.Log.Text);

        // Set aside once: the file is the starter's own again.
        favorites.Toggle(scenery);
        Assert.False(installation.Files.FileExists(FavoritesPath + ".unreadable-2"));
    }

    [Fact]
    public void A_locked_file_is_left_alone_when_no_copy_of_it_can_be_made()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithLegacyFile(TestInstallation.At("scenery", "td.scn"), "//$n Testowa")
            .WithFile(FavoritesPath, "one.scn\ntwo.scn")
            .FailReading(FavoritesPath));
        var scenery = Assert.Single(installation.Sceneries.LoadAll());
        var favorites = new FavoriteSceneries(installation.Files, installation.Log, FavoritesPath);

        favorites.Toggle(scenery);

        installation.Files.AllowReading(FavoritesPath);
        Assert.Equal("one.scn\ntwo.scn", installation.Files.ReadAllText(FavoritesPath));
        Assert.Contains("writing", installation.Log.Text);
    }
}
