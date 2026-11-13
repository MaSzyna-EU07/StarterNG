using StarterNG.Domain.Vehicles;
using StarterNG.Infrastructure.Vehicles;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class TextureFolderTests
{
    private static readonly TestInstallation Installation = new(new InMemoryFileSystem(caseSensitive: true)
        .WithFile("game/dynamic/PKP/EP09_v1/textures.txt", "")
        .WithDirectory("game/Other/Folder"));

    [Theory]
    [InlineData("pkp/ep09_v1/", "dynamic/PKP/EP09_v1")]
    [InlineData(@"PKP\EP09_V1", "dynamic/PKP/EP09_v1")]
    [InlineData("other/folder/", "Other/Folder")]
    [InlineData("pkp/brak/", "pkp/brak")]
    public void A_liverys_folder_is_found_as_it_is_named_on_disk(string named, string onDisk) =>
        Assert.Equal(Path.GetFullPath(Path.Combine("game", onDisk)),
                     Path.GetFullPath(TextureFolder.Of(Installation.Files, Installation.Paths,
                                                       new VehicleTexture { Directory = named })));
}
