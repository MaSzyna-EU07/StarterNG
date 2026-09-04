using StarterNG.Infrastructure.Adapters;
using StarterNG.Infrastructure.Vehicles;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class MiniTextureIndexTests
{
    private static MiniTextureIndex Build(params string[] thumbnails)
    {
        var files = new InMemoryFileSystem();
        foreach (string name in thumbnails)
            files.WithFile(TestInstallation.At("textures", "mini", name + ".bmp"), "bitmap");

        return new MiniTextureIndex(files, new GamePaths(TestInstallation.Root));
    }

    [Fact]
    public void A_thumbnail_is_found_by_name_whatever_its_case()
    {
        var index = Build("ep07", "other");

        Assert.True(index.Has("EP07"));
        Assert.EndsWith("ep07.bmp", index.PathFor("ep07"));
    }

    [Fact]
    public void A_vehicle_naming_an_unknown_thumbnail_gets_other_bmp()
    {
        var index = Build("ep07", "other");

        Assert.False(index.Has("nie-ma-takiej"));
        Assert.EndsWith("other.bmp", index.PathFor("nie-ma-takiej"));
    }

    [Fact]
    public void Without_other_bmp_there_is_nothing_to_fall_back_to()
    {
        var index = Build("ep07");

        // The views draw their own stand-in wagon from here on; see
        // MiniTextures.MissingVisual.
        Assert.Null(index.PathFor("nie-ma-takiej"));
        Assert.Null(index.FallbackPath);
    }

    [Fact]
    public void An_installation_with_no_thumbnails_at_all_resolves_nothing()
    {
        var index = Build();

        Assert.False(index.Has("ep07"));
        Assert.Null(index.PathFor("ep07"));
    }
}
