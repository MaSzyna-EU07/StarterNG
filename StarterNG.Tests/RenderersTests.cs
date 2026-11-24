using StarterNG.Domain.Settings;

namespace StarterNG.Tests;

public class RenderersTests
{
    [Theory]
    [InlineData("default", RendererKind.Modern)]
    [InlineData("full", RendererKind.Modern)]
    [InlineData("vulkan", RendererKind.Modern)]
    [InlineData("legacy", RendererKind.Legacy)]
    [InlineData("simple", RendererKind.Legacy)]
    [InlineData("simpleshader", RendererKind.Legacy)]
    [InlineData("OFF", RendererKind.Legacy)]
    public void A_renderer_word_draws_with_the_renderer_master_gives_it(string word, RendererKind kind) =>
        Assert.Equal(kind, Renderers.KindOf(word));
}
