using StarterNG.Infrastructure;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class LoadingScreenLogoTests
{
    private static InMemoryFileSystem Logos() => new InMemoryFileSystem(caseSensitive: true)
        .WithFile("game/Textures/Logo/EP09_Logo.PNG", "png")
        .WithFile("game/Textures/Logo/Braniewo.JPG", "jpg")
        .WithFile("game/Textures/Logo/Logo1.BMP", "bmp")
        .WithFile("game/Textures/Logo/notes.txt", "");

    [Theory]
    [InlineData("ep09_logo", null, "EP09_Logo.PNG")]
    [InlineData("ep09_logo.bmp", "braniewo", "EP09_Logo.PNG")]
    [InlineData(null, "braniewo", "Braniewo.JPG")]
    [InlineData("brak", "brak", "Logo1.BMP")]
    public void The_logo_is_found_whatever_the_case_of_its_name_and_extension(string? logo, string? scenery,
                                                                             string onDisk) =>
        Assert.Equal(Path.GetFullPath(Path.Combine("game", "Textures", "Logo", onDisk)),
                     Path.GetFullPath(LoadingScreen.FindSource(Logos(), "game", logo, scenery, new Random(1))!));

    [Fact]
    public void With_no_logo_folder_there_is_no_logo() =>
        Assert.Null(LoadingScreen.FindSource(new InMemoryFileSystem(caseSensitive: true), "game", "x", "y",
                                             new Random(1)));
}
