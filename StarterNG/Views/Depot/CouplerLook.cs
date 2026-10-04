using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Material.Icons;
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

/// <summary>
/// How a coupler is drawn on the consist strip, so its state reads at a glance:
/// an icon and a bar under it that ties the two cards, both in the state's colour.
/// Green is left out - on the strip it means selected or turned round.
/// </summary>
public static class CouplerLook
{
    private static readonly IBrush Steel = Solid("#C3CBD3");
    private static readonly IBrush Dim = Solid("#6B737B");
    private static readonly IBrush Sky = Solid("#4FB8FF");
    private static readonly IBrush Violet = Solid("#B08CFF");
    private static readonly IBrush Red = Solid("#FF5A5A");
    private static readonly IBrush Amber = Solid("#F0A030");

    private static IBrush Solid(string color) => new SolidColorBrush(Color.Parse(color));

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

    public static MaterialIconKind Icon(CouplerState state) => state switch
    {
        CouplerState.Free => MaterialIconKind.LinkVariantOff,
        CouplerState.Permanent => MaterialIconKind.LinkLock,
        CouplerState.EndSignals => MaterialIconKind.RecordCircle,
        CouplerState.NoEndSignals => MaterialIconKind.AlertCircleOutline,
        _ => MaterialIconKind.LinkVariant
    };

    public static IBrush Brush(CouplerState state) => state switch
    {
        CouplerState.Free => Dim,
        CouplerState.MultipleUnit => Sky,
        CouplerState.Permanent => Violet,
        CouplerState.EndSignals => Red,
        CouplerState.NoEndSignals => Amber,
        _ => Steel
    };

    /// <summary>The bar under the icon: none at the back, broken where nothing is coupled.</summary>
    public static bool HasBar(CouplerState state) => state is not (CouplerState.EndSignals or CouplerState.NoEndSignals);

    /// <summary>Thicker where the vehicles do not part in shunting.</summary>
    public static double BarThickness(CouplerState state) => state == CouplerState.Permanent ? 5 : 3;

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
