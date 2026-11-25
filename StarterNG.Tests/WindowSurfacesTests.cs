using Avalonia;
using StarterNG.Infrastructure;

namespace StarterNG.Tests;

public class WindowSurfacesTests
{
    [Fact]
    public void Windows_draws_into_the_window_surface_and_never_through_winui_composition()
    {
        var options = WindowSurfaces.Win32Options;

        Assert.Equal(new[] { Win32CompositionMode.RedirectionSurface }, options.CompositionMode);
        Assert.DoesNotContain(Win32CompositionMode.WinUIComposition, options.CompositionMode);
    }

    [Fact]
    public void The_graphics_card_still_draws()
    {
        Assert.Equal(new Win32PlatformOptions().RenderingMode, WindowSurfaces.Win32Options.RenderingMode);
        Assert.Contains(Win32RenderingMode.AngleEgl, WindowSurfaces.Win32Options.RenderingMode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_options_go_to_the_same_builder_with_or_without_the_processor(bool softwareRender)
    {
        var builder = AppBuilder.Configure<Avalonia.Application>();

        Assert.Same(builder, WindowSurfaces.ApplyTo(builder, softwareRender));
    }
}
