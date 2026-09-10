using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace StarterNG.Infrastructure;

public static class MonoFont
{
    private static readonly string[] Preferred =
    {
        "Consolas",
        "Menlo",
        "SF Mono",
        "DejaVu Sans Mono",
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

            string? anyMono = installed.FirstOrDefault(
                n => n.Contains("Mono", StringComparison.OrdinalIgnoreCase));
            if (anyMono is not null)
                return new FontFamily(anyMono);
        }
        catch
        {
        }

        return FontFamily.Default;
    }
}
