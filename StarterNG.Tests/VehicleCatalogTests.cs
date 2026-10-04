using StarterNG.Domain.Vehicles;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class VehicleCatalogTests
{
    private static string TexturesFile(string maker, string vehicle) =>
        TestInstallation.At("dynamic", maker, vehicle, "textures.txt");

    [Fact]
    public void The_catalogue_is_filled_from_every_vehicle_folder()
    {
        var files = new InMemoryFileSystem()
            .WithFile(TexturesFile("pkp", "ep07"), "ep07-135.mat=ep07.e3d,ep07")
            .WithFile(TexturesFile("pkp", "111a"), "111a-1.mat=111a.e3d,111a");
        var installation = new TestInstallation(files);

        int liveries = installation.Vehicles.Load(installation.Library.Vehicles);

        Assert.Equal(2, liveries);
        Assert.Equal(2, installation.Library.Vehicles.Textures.Count);
    }

    [Fact]
    public void The_data_folder_is_recorded_the_way_the_scenery_format_spells_it()
    {
        var files = new InMemoryFileSystem()
            .WithFile(TexturesFile("pkp", "ep07"), "ep07-135.mat=ep07.e3d,ep07");
        var installation = new TestInstallation(files);
        installation.Vehicles.Load(installation.Library.Vehicles);

        Assert.Equal("pkp/ep07/", Assert.Single(installation.Library.Vehicles.Textures).Directory);
    }

    [Fact]
    public void Wrecks_are_indexed_but_kept_out_of_the_offered_liveries()
    {
        var files = new InMemoryFileSystem()
            .WithFile(TexturesFile("pkp", "ep07"), "ep07wrak.mat=ep07.e3d,ep07\nep07-135.mat=ep07.e3d,ep07");
        var installation = new TestInstallation(files);
        installation.Vehicles.Load(installation.Library.Vehicles);

        var catalog = installation.Library.Vehicles;
        Assert.Equal("ep07-135", Assert.Single(catalog.Textures).Skinfile);
        Assert.NotNull(catalog.TextureForSkin("ep07wrak"));
    }

    [Fact]
    public void A_livery_is_found_by_its_skin_file_whatever_the_extension()
    {
        var files = new InMemoryFileSystem()
            .WithFile(TexturesFile("pkp", "ep07"), "ep07-135.mat=ep07.e3d,ep07");
        var installation = new TestInstallation(files);
        installation.Vehicles.Load(installation.Library.Vehicles);

        Assert.NotNull(installation.Library.Vehicles.TextureForSkin("EP07-135.bmp"));
        Assert.Null(installation.Library.Vehicles.TextureForSkin("nieznany"));
    }

    [Fact]
    public void A_star_category_is_derived_from_the_first_letter_of_the_thumbnail()
    {
        var catalog = new VehicleCatalog(new MiniStub());
        catalog.BeginLoad();
        catalog.Ingest(ParsedEntry("pkp/ep07/", "ep07-135.mat=ep07.e3d,ep07"));
        catalog.EndLoad();

        Assert.Equal("E", Assert.Single(catalog.Textures).ResolvedCategory);
    }

    [Fact]
    public void Members_of_a_set_after_the_first_are_followers()
    {
        var files = new InMemoryFileSystem()
            .WithFile(TexturesFile("pkp", "en57"),
                      "^2\nen57-a.mat=en57.e3d,en57\nen57-b.mat=en57.e3d,en57");
        var installation = new TestInstallation(files);
        installation.Vehicles.Load(installation.Library.Vehicles);
        var catalog = installation.Library.Vehicles;

        var lead = catalog.TextureForSkin("en57-a")!;
        var follower = catalog.TextureForSkin("en57-b")!;

        Assert.False(catalog.IsSetFollower(lead));
        Assert.True(catalog.IsSetFollower(follower));
        Assert.Equal(2, catalog.ResolveSet(lead)!.Count);
    }

    [Fact]
    public void A_livery_falls_back_to_its_groups_thumbnail_when_its_own_is_missing()
    {
        var catalog = new VehicleCatalog(new MiniStub(known: "ep07"));
        catalog.BeginLoad();
        catalog.Ingest(ParsedEntry("pkp/ep07/", "ep07-135.mat=ep07.e3d,ep07,brakujacy"));
        catalog.EndLoad();

        Assert.Equal("ep07", catalog.ResolveMiniName(Assert.Single(catalog.Textures)));
    }

    /// <summary>Thumbnail index that knows about exactly one name.</summary>
    private sealed class MiniStub : StarterNG.Application.Abstractions.IMiniTextureIndex
    {
        private readonly string? _known;

        public MiniStub(string? known = null) => _known = known;

        public void Preload() { }

        public bool Has(string? miniName) => miniName is not null && miniName == _known;

        public string? PathFor(string? miniName) => Has(miniName) ? miniName : FallbackPath;

        public string? FallbackPath => null;
    }

    /// <summary>Builds an entry through the real parser, for catalogue-level tests.</summary>
    private static VehicleEntry ParsedEntry(string directory, params string[] lines) =>
        new StarterNG.Infrastructure.Vehicles.TexturesTxtParser().Parse(directory, lines)!;

    // The ET42-026: one skin for both halves, told apart by the model.
    private static VehicleCatalog SharedSkinPair()
    {
        var files = new InMemoryFileSystem()
            .WithFile(TexturesFile("pkp", "et42_v2"),
                      "!=e,ET42-A,ET42-B\n^2\n112E-026.mat=112E-A,ET42-A,ET42-026-A\n112E-026.mat=112E-B,ET42-B,ET42-026-B");
        var installation = new TestInstallation(files);
        installation.Vehicles.Load(installation.Library.Vehicles);
        return installation.Library.Vehicles;
    }

    [Fact]
    public void A_skin_shared_by_two_models_is_found_by_the_model()
    {
        var catalog = SharedSkinPair();

        Assert.Equal("ET42-026-A", catalog.TextureFor("112E-026", "112E-A")!.TextureMini);
        Assert.Equal("ET42-026-B", catalog.TextureFor("112E-026", "112E-B")!.TextureMini);
    }

    [Fact]
    public void The_second_half_on_a_shared_skin_follows_the_first_in_its_set()
    {
        var catalog = SharedSkinPair();
        var b = catalog.TextureFor("112E-026", "112E-B")!;

        Assert.True(catalog.IsSetFollower(b));
        Assert.Equal(new[] { "112E-A", "112E-B" }, catalog.ResolveSet(b)!.Select(t => t.Model));
    }
}
