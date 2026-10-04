using System.Text;
using StarterNG.Domain.Vehicles;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class VehicleCacheTests
{
    private const string Ep07 = "ep07-135.mat=ep07.e3d,ep07";

    private const string En57 = "en57-1000ra.mat=en57.e3d,en57";

    private static string Dynamic(string maker, string vehicle) =>
        TestInstallation.At("dynamic", maker, vehicle, "textures.txt");

    private static string Cached(TestInstallation installation, string file) =>
        Path.Combine(installation.SettingsPaths.VehicleCacheDirectory(), file);

    [Fact]
    public void The_first_load_migrates_textures_txt_into_the_starters_own_copy()
    {
        var files = new InMemoryFileSystem().WithLegacyFile(Dynamic("pkp", "ep07"), Ep07);
        var installation = new TestInstallation(files);

        int liveries = installation.VehicleCache.Load(installation.Library.Vehicles);

        Assert.Equal(1, liveries);
        Assert.True(files.FileExists(Cached(installation, "vehicles.json")));
        Assert.Contains("ep07-135", files.ReadAllText(Cached(installation, "vehicles.json")));
    }

    [Fact]
    public void An_untouched_installation_is_served_from_the_copy_without_reading_textures_txt()
    {
        var files = new InMemoryFileSystem().WithLegacyFile(Dynamic("pkp", "ep07"), Ep07);
        var installation = new TestInstallation(files);
        installation.VehicleCache.Load(installation.Library.Vehicles);

        // Different liveries, same length and same timestamp: nothing the stamp
        // can see has changed, so an answer of ep07-135 can only come from the copy.
        files.WithLegacyFile(Dynamic("pkp", "ep07"), "ep07-999.mat=ep07.e3d,ep07");
        var catalog = new VehicleCatalog(installation.MiniTextures);
        installation.VehicleCache.Load(catalog);

        Assert.Equal("legacy:pkp/ep07/ep07-135", Assert.Single(catalog.Textures).Uuid);
    }

    [Fact]
    public void An_edited_textures_txt_makes_the_copy_stale_and_it_is_rebuilt()
    {
        var files = new InMemoryFileSystem().WithLegacyFile(Dynamic("pkp", "ep07"), Ep07);
        var installation = new TestInstallation(files);
        installation.VehicleCache.Load(installation.Library.Vehicles);

        files.WithLegacyFile(Dynamic("pkp", "ep07"), Ep07 + "\nep07-174.mat=ep07.e3d,ep07");
        var catalog = new VehicleCatalog(installation.MiniTextures);
        int liveries = installation.VehicleCache.Load(catalog);

        Assert.Equal(2, liveries);
        Assert.Contains("ep07-174", files.ReadAllText(Cached(installation, "vehicles.json")));
    }

    [Fact]
    public void A_vehicle_folder_that_disappeared_upstream_leaves_the_copy_too()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Dynamic("pkp", "ep07"), Ep07)
            .WithLegacyFile(Dynamic("pkp", "en57"), En57);
        var installation = new TestInstallation(files);
        installation.VehicleCache.Load(installation.Library.Vehicles);

        files.DeleteFile(Dynamic("pkp", "en57"));
        var catalog = new VehicleCatalog(installation.MiniTextures);
        installation.VehicleCache.Load(catalog);

        Assert.Equal("legacy:pkp/ep07/ep07-135", Assert.Single(catalog.Textures).Uuid);
        Assert.DoesNotContain("en57", files.ReadAllText(Cached(installation, "vehicles.json")));
    }

    [Fact]
    public void A_damaged_copy_costs_the_parse_it_saved_and_nothing_else()
    {
        var files = new InMemoryFileSystem().WithLegacyFile(Dynamic("pkp", "ep07"), Ep07);
        var installation = new TestInstallation(files);
        installation.VehicleCache.Load(installation.Library.Vehicles);
        files.WithFile(Cached(installation, "vehicles.json"), "{ obcieta kop");

        var catalog = new VehicleCatalog(installation.MiniTextures);
        int liveries = installation.VehicleCache.Load(catalog);

        Assert.Equal(1, liveries);
        Assert.Contains("kopia nie do odczytu", installation.Log.Text);
    }

    [Fact]
    public void The_copy_reads_the_same_catalogue_the_legacy_reader_does()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Dynamic("pkp", "ep07"), Ep07)
            .WithLegacyFile(Dynamic("pkp", "en57"), En57);
        var installation = new TestInstallation(files);

        var direct = new VehicleCatalog(installation.MiniTextures);
        installation.Vehicles.Load(direct);
        var cached = new VehicleCatalog(installation.MiniTextures);
        installation.VehicleCache.Load(cached);

        Assert.Equal(direct.Textures.Select(t => t.Uuid), cached.Textures.Select(t => t.Uuid));
        Assert.Equal(direct.GroupsById.Keys.Order(), cached.GroupsById.Keys.Order());
    }
}
