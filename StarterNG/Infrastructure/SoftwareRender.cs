using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace StarterNG.Infrastructure;

/// <summary>
/// Drawn by the processor instead of the graphics card, for a machine on which the
/// window comes up empty. Each platform reads only its own options, so both are given.
/// </summary>
public static class SoftwareRender
{
    public const string Switch = "--software-render";

    public static bool AskedIn(IEnumerable<string> args) =>
        args.Any(a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));

    public static Win32PlatformOptions Win32Options => new()
    {
        RenderingMode = new[] { Win32RenderingMode.Software },
        CompositionMode = new[] { Win32CompositionMode.RedirectionSurface }
    };

    public static X11PlatformOptions X11Options => new()
    {
        RenderingMode = new[] { X11RenderingMode.Software }
    };

    public static AppBuilder ApplyTo(AppBuilder builder) =>
        builder.With(Win32Options).With(X11Options);
}
