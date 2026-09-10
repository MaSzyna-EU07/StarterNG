using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class PhysicsAndAssetsTests
{
    private static string Fiz(string maker, string vehicle, string name) =>
        TestInstallation.At("dynamic", maker, vehicle, name + ".fiz");

    [Fact]
    public void A_fiz_yields_the_properties_the_starter_shows()
    {
        const string fiz = """
            Param: M=80000 Vmax=125
            Dimensions: L=15.9
            Load: LoadAccepted=Coal MaxLoad=40
            Engine: EngineType=DieselElectric
            """;
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(Fiz("pkp", "sm42", "sm42"), fiz));

        var physics = installation.Physics.For("pkp/sm42/", "sm42.e3d");

        Assert.NotNull(physics);
        Assert.Equal(80000, physics!.Mass);
        Assert.Equal(125, physics.VMax);
        Assert.Equal(15.9, physics.Length);
        Assert.Equal("Coal", physics.LoadAccepted);
        Assert.Equal(40, physics.MaxLoad);
        Assert.True(physics.IsDieselEngine);
    }

    [Fact]
    public void A_vehicle_declaring_one_coupler_gets_the_same_at_both_ends()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(Fiz("pkp", "111a", "111a"), "BuffCoupl1: AllowedFlag=3 ControlType=screw"));

        var physics = installation.Physics.For("pkp/111a/", "111a")!;

        Assert.Equal(3, physics.AllowedFlagA);
        Assert.Equal(3, physics.AllowedFlagB);
        Assert.Equal("screw", physics.ControlTypeB);
    }

    [Fact]
    public void A_negative_coupler_flag_is_encoded_as_disabled()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(Fiz("pkp", "111a", "111a"), "BuffCoupl1: AllowedFlag=-3"));

        Assert.Equal(131, installation.Physics.For("pkp/111a/", "111a")!.AllowedFlagA);
    }

    [Fact]
    public void An_include_is_followed_and_its_parameters_substituted()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(Fiz("pkp", "ep07", "ep07"), "include common.fiz 84000 end")
            .WithFile(TestInstallation.At("dynamic", "pkp", "ep07", "common.fiz"), "Param: M=(1) Vmax=125"));

        var physics = installation.Physics.For("pkp/ep07/", "ep07")!;

        Assert.Equal(84000, physics.Mass);
        Assert.Equal(125, physics.VMax);
    }

    [Fact]
    public void A_dumb_variant_stands_in_when_the_model_has_no_fiz_of_its_own()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(Fiz("pkp", "ep07", "ep07dumb"), "Param: M=1000"));

        Assert.NotNull(installation.Physics.For("pkp/ep07/", "ep07"));
    }

    [Fact]
    public void An_unknown_model_has_no_physics_and_an_empty_installation_indexes_nothing()
    {
        var installation = new TestInstallation();

        Assert.Null(installation.Physics.For("pkp/x/", "nieznany"));
        installation.Physics.Preload();
        Assert.Equal(0, installation.Physics.IndexedCount);
    }

    [Fact]
    public void Thumbnails_are_looked_up_by_name_with_a_fallback_to_other()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("textures", "mini", "ep07.bmp"), "bitmap")
            .WithFile(TestInstallation.At("textures", "mini", "other.bmp"), "bitmap"));

        Assert.True(installation.MiniTextures.Has("EP07"));
        Assert.False(installation.MiniTextures.Has("nieznany"));
        Assert.EndsWith("ep07.bmp", installation.MiniTextures.PathFor("ep07"));
        Assert.EndsWith("other.bmp", installation.MiniTextures.PathFor("nieznany"));
    }

    [Fact]
    public void A_livery_whose_skin_and_model_are_absent_is_reported()
    {
        var files = new InMemoryFileSystem()
            .WithFile(TestInstallation.At("dynamic", "pkp", "ep07", "textures.txt"),
                      "ep07-135.mat=ep07.e3d,ep07");
        var installation = new TestInstallation(files);
        installation.Vehicles.Load(installation.Library.Vehicles);

        var missing = installation.MissingAssets.Scan(installation.Library.Vehicles);

        Assert.Contains(missing, line => line.Contains("no file:") && line.Contains("ep07-135"));
        Assert.Contains(missing, line => line.Contains("no model:") && line.Contains("ep07.e3d"));
    }

    [Fact]
    public void A_skin_present_under_an_accepted_extension_is_not_reported()
    {
        var files = new InMemoryFileSystem()
            .WithFile(TestInstallation.At("dynamic", "pkp", "ep07", "textures.txt"),
                      "ep07-135.mat=ep07.e3d,ep07")
            .WithFile(TestInstallation.At("dynamic", "pkp", "ep07", "ep07-135.bmp"), "bitmap")
            .WithFile(TestInstallation.At("dynamic", "pkp", "ep07", "ep07.t3d"), "model");
        var installation = new TestInstallation(files);
        installation.Vehicles.Load(installation.Library.Vehicles);

        Assert.Empty(installation.MissingAssets.Scan(installation.Library.Vehicles));
    }
}
