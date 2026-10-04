using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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
/// How a coupler is drawn on the consist strip. Under an icon for the coupler as a
/// whole runs a bundle of stripes, one per connection in its own colour, so what
/// is coupled reads at a glance; the coupler editor shows the same colours as its
/// legend. Green is left out - on the strip it means selected or turned round.
/// </summary>
public static class CouplerLook
{
    private static readonly IBrush Steel = Solid("#C3CBD3");
    private static readonly IBrush Dim = Solid("#6B737B");
    private static readonly IBrush Red = Solid("#FF5A5A");
    private static readonly IBrush Amber = Solid("#F0A030");

    /// <summary>One per bit of <see cref="CouplingBits.BitKeys"/>, in the same order.</summary>
    private static readonly IBrush[] BitBrushes =
    {
        Solid("#C3CBD3"), // mechanical - steel
        Solid("#FF5A5A"), // brake pipe - red
        Solid("#4FB8FF"), // multiple-unit control - blue
        Solid("#FFD43B"), // high voltage - yellow
        Solid("#B08CFF"), // gangway - violet
        Solid("#FF9F43"), // auxiliary air - orange
        Solid("#FF6FB5"), // heating - pink
        Solid("#7A838C")  // workshop lock - dark steel
    };

    private static IBrush Solid(string color) => new SolidColorBrush(Color.Parse(color));

    public static IBrush BitBrush(int index) => BitBrushes[index];

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
        CouplerState.EndSignals => Red,
        CouplerState.NoEndSignals => Amber,
        _ => Steel
    };

    /// <summary>The bits that are set, as indexes into <see cref="CouplingBits.BitKeys"/>.</summary>
    public static IEnumerable<int> SetBits(Coupling coupling) =>
        Enumerable.Range(0, CouplingBits.BitKeys.Length).Where(i => coupling.Has(1 << i));

    /// <summary>A connection's name with its colour beside it, for the editor's checkboxes.</summary>
    public static Control BitLabel(int index) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children =
        {
            new Border
            {
                Width = 10, Height = 10, CornerRadius = new CornerRadius(2),
                Background = BitBrushes[index], VerticalAlignment = VerticalAlignment.Center
            },
            new TextBlock { Text = App.Loc[CouplingBits.BitKeys[index]], VerticalAlignment = VerticalAlignment.Center }
        }
    };

    /// <summary>What the coupler connects, by the names the coupler editor uses.</summary>
    public static IReadOnlyList<string> Connections(Coupling coupling) =>
        SetBits(coupling).Select(i => App.Loc[CouplingBits.BitKeys[i]]).ToList();

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
