using System.Collections.Generic;
using System.Linq;

namespace StarterNG.Domain.Settings;

/// <summary>A size in pixels, as width and height in eu07.ini take it.</summary>
public readonly record struct ScreenSize(int Width, int Height)
{
    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>A size to offer for the simulator's window.</summary>
/// <param name="IsMonitor">A monitor in the computer has exactly this size.</param>
public readonly record struct ResolutionChoice(ScreenSize Size, bool IsMonitor);

/// <summary>
/// What the resolution list offers: the monitors in the computer first, then the
/// usual sizes that fit on one of them, largest first. A full-screen simulator gets
/// the closest mode its monitor has, so a size no monitor can show is of no use.
/// </summary>
public static class ResolutionChoices
{
    private static readonly ScreenSize[] Usual =
    {
        // 16:9
        new(3840, 2160), new(2560, 1440), new(1920, 1080), new(1600, 900), new(1366, 768), new(1280, 720),
        // 16:10
        new(2560, 1600), new(1920, 1200), new(1680, 1050), new(1440, 900), new(1280, 800),
        // Ultrawide
        new(3440, 1440), new(2560, 1080),
        // 5:4 and 4:3
        new(1280, 1024), new(1024, 768)
    };

    /// <param name="monitors">The monitors, the primary one first; empty where they cannot be told.</param>
    /// <param name="current">The size asked for now, kept on the list whatever it is.</param>
    public static IReadOnlyList<ResolutionChoice> For(IReadOnlyCollection<ScreenSize> monitors, ScreenSize current)
    {
        var known = monitors.Where(IsReal).Distinct().ToList();
        var rest = Usual.Where(size => known.Count == 0 || known.Any(m => Fits(size, m)));
        if (IsReal(current))
            rest = rest.Append(current);

        return known.Select(size => new ResolutionChoice(size, true))
            .Concat(rest.Except(known).Distinct()
                .OrderByDescending(size => size.Width)
                .ThenByDescending(size => size.Height)
                .Select(size => new ResolutionChoice(size, false)))
            .ToList();
    }

    private static bool IsReal(ScreenSize size) => size.Width > 0 && size.Height > 0;

    private static bool Fits(ScreenSize size, ScreenSize monitor) =>
        size.Width <= monitor.Width && size.Height <= monitor.Height;
}
