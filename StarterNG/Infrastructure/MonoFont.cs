using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace StarterNG.Infrastructure;

/// <summary>
/// A fixed-width font family that actually exists on this machine.
/// Avalonia treats the extra names in "Consolas, monospace" as glyph fallbacks,
/// not as family alternatives, and "monospace" is a fontconfig alias it never
/// resolves — so that stack silently renders in the proportional UI font
/// everywhere except Windows. The timetable and the fault/preview boxes align
/// their columns with spaces, so they need a real monospace family: probe the
/// installed ones once and keep the first match.
/// </summary>
public static class MonoFont
{
    private static readonly string[] Preferred =
    {
        "Consolas",         // Windows
        "Menlo",            // macOS
        "SF Mono",
        "DejaVu Sans Mono", // most Linux distros
        "Liberation Mono",
        "Noto Sans Mono",
        "Ubuntu Mono",
        "Courier New"
    };

    public static FontFamily Family { get; } = Resolve();

    private static FontFamily Resolve()
    {
        try
        {
            var installed = new HashSet<string>(
                FontManager.Current.SystemFonts.Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (string name in Preferred)
                if (installed.Contains(name))
                    return new FontFamily(name);

            // None of the usual suspects: take any family that calls itself Mono.
            string? anyMono = installed.FirstOrDefault(
                n => n.Contains("Mono", StringComparison.OrdinalIgnoreCase));
            if (anyMono is not null)
                return new FontFamily(anyMono);
        }
        catch { /* no font manager yet - fall back to the default family */ }

        return FontFamily.Default;
    }
}
