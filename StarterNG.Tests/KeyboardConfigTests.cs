using StarterNG.Classes;

namespace StarterNG.Tests;

public class KeyboardConfigTests
{
    [Fact]
    public void A_description_without_polish_letters_matches_the_bundled_one() =>
        Assert.Equal("zwiekszenie nastawnika glownego", KeyboardConfig.StripPolish("zwiększenie nastawnika głównego"));
}
