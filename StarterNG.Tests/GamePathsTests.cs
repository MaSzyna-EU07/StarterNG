using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Tests;

public class GamePathsTests
{
    [Fact]
    public void Wellknown_folders_hang_off_the_installation_root()
    {
        var paths = new GamePaths(Path.Combine("opt", "maszyna"));

        Assert.Equal(Path.Combine("opt", "maszyna", "scenery"), paths.Scenery);
        Assert.Equal(Path.Combine("opt", "maszyna", "dynamic"), paths.Dynamic);
        Assert.Equal(Path.Combine("opt", "maszyna", "textures", "mini"), paths.MiniTextures);
        Assert.Equal(Path.Combine("opt", "maszyna", "starter", "bledy.txt"), paths.DiagnosticsLog);
    }

    [Fact]
    public void FromRoot_joins_segments_below_the_root()
    {
        var paths = new GamePaths("game");

        Assert.Equal(Path.Combine("game", "data", "load_weights.txt"),
                     paths.FromRoot("data", "load_weights.txt"));
    }
}
