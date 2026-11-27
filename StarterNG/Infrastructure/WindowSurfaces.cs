using Avalonia;

namespace StarterNG.Infrastructure;

/// <summary>
/// On Windows the graphics card draws each window of the starter into a surface of that
/// window, not into Avalonia's default WinUI composition visual. On Windows 11 25H2 that
/// visual was left empty on many starts: the main window, or a dialog opened later,
/// stayed black while the others drew, and came back only now and then. Composition is
/// there for see-through windows and backdrops, and the starter has none.
/// </summary>
public static class WindowSurfaces
{
    public static Win32PlatformOptions Win32Options => new()
    {
        CompositionMode = new[] { Win32CompositionMode.RedirectionSurface }
    };

    /// <summary>
    /// The options a platform reads are the last given, so the processor, when asked
    /// for, draws with <see cref="SoftwareRender"/>'s own and these are left out.
    /// </summary>
    public static AppBuilder ApplyTo(AppBuilder builder, bool softwareRender) =>
        softwareRender ? SoftwareRender.ApplyTo(builder) : builder.With(Win32Options);
}
