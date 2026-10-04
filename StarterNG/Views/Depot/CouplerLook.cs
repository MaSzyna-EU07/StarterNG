using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using StarterNG.Classes;

namespace StarterNG.Views;

public enum CouplerState
{
    /// <summary>Nothing connected between the two vehicles.</summary>
    Free,

    /// <summary>Coupled the ordinary way: mechanically, the brake pipe and so on.</summary>
    Coupled,

    /// <summary>Coupled with multiple-unit control.</summary>
    MultipleUnit,

    /// <summary>Not to be parted in shunting: workshop lock, or a locked coupler.</summary>
    Permanent,

    /// <summary>The back of the train at 0 - the simulator puts up the end signals.</summary>
    EndSignals,

    /// <summary>The back of the train at anything but 0 - no end signals, and nothing else.</summary>
    NoEndSignals
}

/// <summary>How a coupler is drawn on the consist strip, so its state reads at a glance.</summary>
public static class CouplerLook
{
    private static readonly IBrush Dim = new SolidColorBrush(Color.Parse("#808080"));
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#41C400"));
    private static readonly IBrush Blue = new SolidColorBrush(Color.Parse("#4AA3FF"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#E5484D"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#E0A030"));

    public static CouplerState StateOf(Coupling coupling, bool trailing)
    {
        int flags = coupling.AbsFlags;
        if (trailing)
            return flags == 0 ? CouplerState.EndSignals : CouplerState.NoEndSignals;
        if (flags == 0)
            return CouplerState.Free;
        if (coupling.Locked || (flags & Coupling.WorkshopLock) != 0)
            return CouplerState.Permanent;
        return (flags & Coupling.ControlMU) != 0 ? CouplerState.MultipleUnit : CouplerState.Coupled;
    }

    // Box drawing and a plain disc: Windows draws no colour emoji for these.
    public static string Glyph(CouplerState state) => state switch
    {
        CouplerState.Free => "╎",
        CouplerState.Permanent => "═",
        CouplerState.EndSignals => "●",
        _ => "≣"
    };

    /// <summary>The colour of the glyph; null keeps the ordinary text colour.</summary>
    public static IBrush? Brush(CouplerState state) => state switch
    {
        CouplerState.Free => Dim,
        CouplerState.MultipleUnit => Green,
        CouplerState.Permanent => Blue,
        CouplerState.EndSignals => Red,
        CouplerState.NoEndSignals => Amber,
        _ => null
    };

    /// <summary>What the coupler connects, by the names the coupler editor uses.</summary>
    public static IReadOnlyList<string> Connections(Coupling coupling) =>
        Enumerable.Range(0, CouplingBits.BitKeys.Length)
                  .Where(i => coupling.Has(1 << i))
                  .Select(i => App.Loc[CouplingBits.BitKeys[i]])
                  .ToList();

    /// <summary>The tooltip: the code as the scenery has it, then what it means.</summary>
    public static string Describe(Coupling coupling, bool trailing)
    {
        string connections = string.Join(", ", Connections(coupling));
        string meaning = StateOf(coupling, trailing) switch
        {
            CouplerState.Free => App.Loc["CouplerFree"],
            CouplerState.EndSignals => App.Loc["CouplerEndSignals"],
            CouplerState.NoEndSignals => App.Loc["CouplerNoEndSignals"],
            _ => string.Format(App.Loc["CouplerConnects"], connections)
        };

        var lines = new List<string> { string.Format(App.Loc["CouplerCode"], coupling.Flags), meaning };
        if (coupling.Locked && coupling.AbsFlags != 0)
            lines.Add(App.Loc["CouplerLocked"]);
        lines.Add(App.Loc["CouplerClickToEdit"]);
        return string.Join("\n", lines);
    }
}
