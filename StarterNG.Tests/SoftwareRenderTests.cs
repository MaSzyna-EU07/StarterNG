using Avalonia;
using StarterNG.Infrastructure;

namespace StarterNG.Tests;

public class SoftwareRenderTests
{
    [Theory]
    [InlineData("--software-render")]
    [InlineData("--Software-Render")]
    public void The_switch_asks_for_it_in_any_case(string arg)
    {
        Assert.True(SoftwareRender.AskedIn(new[] { "--other", arg }));
    }

    [Fact]
    public void Without_the_switch_the_graphics_card_draws()
    {
        Assert.False(SoftwareRender.AskedIn(Array.Empty<string>()));
        Assert.False(SoftwareRender.AskedIn(new[] { "--software" }));
    }

    [Fact]
    public void Windows_draws_on_the_processor_into_a_plain_window_surface()
    {
        var options = SoftwareRender.Win32Options;

        Assert.Equal(new[] { Win32RenderingMode.Software }, options.RenderingMode);
        Assert.Equal(new[] { Win32CompositionMode.RedirectionSurface }, options.CompositionMode);
    }

    [Fact]
    public void X11_draws_on_the_processor()
    {
        Assert.Equal(new[] { X11RenderingMode.Software }, SoftwareRender.X11Options.RenderingMode);
    }

    [Fact]
    public void Both_platforms_options_go_to_the_same_builder()
    {
        var builder = AppBuilder.Configure<Avalonia.Application>();

        Assert.Same(builder, SoftwareRender.ApplyTo(builder));
    }
}
