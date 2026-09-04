using StarterNG.Infrastructure;
using StarterNG.Infrastructure.Adapters;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class InstallationCheckTests
{
    [Fact]
    public void An_empty_folder_reports_every_missing_game_directory()
    {
        var strings = new FakeStrings().With("FaultNoDir", "missing {0}");
        var check = new InstallationCheck(new GamePaths("game"), new InMemoryFileSystem(), strings);

        var faults = check.Run();

        Assert.Contains("missing /dynamic", faults);
        Assert.Contains("missing /scenery", faults);
        Assert.Contains("FaultNoWeights", faults);
    }

    [Fact]
    public void A_complete_installation_reports_no_missing_directories()
    {
        var files = new InMemoryFileSystem()
            .WithDirectory(Path.Combine("game", "dynamic"))
            .WithDirectory(Path.Combine("game", "sounds"))
            .WithDirectory(Path.Combine("game", "models"))
            .WithDirectory(Path.Combine("game", "scenery"))
            .WithDirectory(Path.Combine("game", "textures"))
            .WithFile(Path.Combine("game", "data", "load_weights.txt"), "");
        var strings = new FakeStrings().With("FaultNoDir", "missing {0}");

        var faults = new InstallationCheck(new GamePaths("game"), files, strings).Run();

        Assert.DoesNotContain(faults, fault => fault.StartsWith("missing "));
        Assert.DoesNotContain("FaultNoWeights", faults);
    }
}
