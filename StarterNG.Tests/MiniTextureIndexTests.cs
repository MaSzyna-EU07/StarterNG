using StarterNG.Infrastructure.Adapters;
using StarterNG.Infrastructure.Vehicles;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class MiniTextureIndexTests
{
    private static readonly string[] Names =
    {
        "ep07", "et22", "111a", "112a", "203v", "401k", "en57", "sm42", "st44", "412w",
        "406r", "424z", "621z", "z1kd", "gags", "eaos", "falns", "uacs", "sgs", "kbs"
    };

    private static MiniTextureIndex Build(int? missRate)
    {
        var files = new InMemoryFileSystem();
        foreach (string name in Names)
            files.WithFile(TestInstallation.At("textures", "mini", name + ".bmp"), "bitmap");
        files.WithFile(TestInstallation.At("textures", "mini", "other.bmp"), "bitmap");

        var environment = new FakeEnvironment();
        if (missRate is { } rate)
            environment.With(MiniTextureIndex.MissRateVariable, rate.ToString());

        return new MiniTextureIndex(files, new GamePaths(TestInstallation.Root), environment);
    }

    [Fact]
    public void Setting_the_variable_to_zero_shows_the_installation_as_it_is()
    {
        var index = Build(missRate: 0);

        Assert.Equal(0, index.SimulatedMissRate);
        Assert.All(Names, name => Assert.True(index.Has(name)));
    }

    [Fact]
    public void The_temporary_default_still_hides_half_the_thumbnails()
    {
        // Guards the testing default in MiniTextureIndex.DefaultMissRate. When
        // that goes back to 0 for release, this test is what says so - change it
        // together with the constant, do not delete it quietly.
        var index = Build(missRate: null);

        Assert.Equal(50, index.SimulatedMissRate);
        Assert.Contains(Names, name => !index.Has(name));
    }

    [Fact]
    public void The_debug_variable_hides_roughly_the_share_it_names()
    {
        var index = Build(missRate: 50);

        int hidden = Names.Count(name => !index.Has(name));

        // Twenty names is a small sample, so this only asserts that the switch
        // bites and does not take everything with it.
        Assert.InRange(hidden, 3, Names.Length - 3);
    }

    [Fact]
    public void A_hidden_thumbnail_stays_hidden_rather_than_flickering()
    {
        var index = Build(missRate: 50);
        string hidden = Names.First(name => !index.Has(name));

        for (int i = 0; i < 20; i++)
            Assert.False(index.Has(hidden));
    }

    [Fact]
    public void A_hidden_thumbnail_falls_back_to_other_bmp_like_an_unknown_one()
    {
        var index = Build(missRate: 50);
        string hidden = Names.First(name => !index.Has(name));

        Assert.EndsWith("other.bmp", index.PathFor(hidden));
    }

    [Fact]
    public void An_unknown_name_still_falls_back_to_other_bmp_in_a_normal_run()
    {
        var index = Build(missRate: 0);

        Assert.EndsWith("other.bmp", index.PathFor("nie-ma-takiej"));
    }
}
