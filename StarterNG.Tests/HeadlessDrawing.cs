using Avalonia;
using Avalonia.Headless;

namespace StarterNG.Tests;

/// <summary>
/// Text and drawing for tests of controls that draw themselves: the headless platform
/// with its stand-in fonts, set up once, on the thread the control tests run on.
/// </summary>
public static class HeadlessDrawing
{
    private static readonly object Gate = new();
    private static bool _ready;

    public static void Ensure()
    {
        lock (Gate)
        {
            if (_ready)
                return;
            AppBuilder.Configure<Avalonia.Application>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
                .SetupWithoutStarting();
            _ready = true;
        }
    }
}
