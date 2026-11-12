using Avalonia.Media;

namespace Eu07.BlackBox.Views;

/// <summary>
/// The colours the map, the timeline and the panels share: those of the starter's Carbon
/// look for the window, its green for what is picked, and colours of their own for the data.
/// </summary>
public static class Palette
{
    // The window, the cards on it and the lines between.
    public static readonly IBrush Background = Brush("#15171B");
    public static readonly IBrush Panel = Brush("#1E2327");
    public static readonly IBrush Card = Brush("#1E2327");
    public static readonly IBrush CardHover = Brush("#2A3036");
    public static readonly IBrush CardBorder = Brush("#3A424A");
    public static readonly IBrush Grid = Brush("#2A3036");
    public static readonly IBrush MapBackground = Brush("#121417");
    public static readonly IBrush Text = Brush("#E6E8EA");
    public static readonly IBrush DimText = Brush("#9098A0");
    public static readonly IBrush FaintText = Brush("#646C74");
    /// <summary>The headers of the panels, as the group boxes of the starter.</summary>
    public static readonly IBrush Header = Brush("#2A3036");
    /// <summary>The green of what is picked or on: bright for lines and text, deep for fills.</summary>
    public static readonly IBrush Accent = Brush("#41C400");
    public static readonly IBrush AccentFill = Brush("#177F00");
    public static readonly IBrush AccentFillHover = Brush("#1E9000");
    public static readonly IBrush AccentText = Brush("#FFFFFF");
    public static readonly IBrush Good = Brush("#4cc46e");

    /// <summary>A delay: green on time, yellow a little late, red late, blue early.</summary>
    public static IBrush Delay(double? minutes) => minutes switch
    {
        null => Good,
        > 3 => Bad,
        > 0.5 => Warn,
        < -0.5 => Station,
        _ => Good
    };

    public static readonly IBrush Warn = Brush("#e8c547");
    public static readonly IBrush Bad = Brush("#f0584a");

    public static readonly IBrush Track = Brush("#6f7b88");
    public static readonly IBrush TrackCasing = Brush("#1d232b");
    public static readonly IBrush SwitchOff = Brush("#323a44");
    public static readonly IBrush Vehicle = Brush("#c9a26b");
    public static readonly IBrush VehicleAi = Brush("#9fb0c3");
    public static readonly IBrush UserTrain = Brush("#ff9d1c");
    public static readonly IBrush Cursor = Brush("#ffb000");

    public static readonly IBrush Within = Brush("#4cc46e");
    public static readonly IBrush Slightly = Brush("#e8c547");
    public static readonly IBrush Over = Brush("#f0584a");
    public static readonly IBrush Autopilot = Brush("#4a90e2");
    public static readonly IBrush NoData = Brush("#5f6871");

    public static readonly IBrush Speed = Brush("#f2f4f6");
    public static readonly IBrush SpeedFill = new SolidColorBrush(Color.Parse("#f2f4f6"), 0.06).ToImmutable();
    public static readonly IBrush Target = Brush("#4cc46e");
    public static readonly IBrush Limit = Brush("#ff8c2a");

    public static readonly IBrush SignalStop = Brush("#ff3b30");
    public static readonly IBrush SignalLimited = Brush("#ffcc00");
    public static readonly IBrush SignalFree = Brush("#34c759");
    public static readonly IBrush SignalShunt = Brush("#e8f0ff");
    public static readonly IBrush SignalShuntStop = Brush("#4a6cff");
    public static readonly IBrush SignalUnknown = Brush("#808890");

    public static readonly IBrush StopPoint = Brush("#f4f6f8");
    public static readonly IBrush Station = Brush("#5aa9ff");
    public static readonly IBrush HornPoint = Brush("#b9a6ff");

    public static readonly IBrush Brakes = Brush("#e05a52");
    public static readonly IBrush Controller = Brush("#2bb3a3");
    public static readonly IBrush Vigilance = Brush("#e8c547");
    public static readonly IBrush CabSignal = Brush("#f08a3a");
    public static readonly IBrush LampOff = Brush("#252c35");
    public static readonly IBrush LampOn = Brush("#4cc46e");
    public static readonly IBrush LampWarning = Brush("#e8c547");
    public static readonly IBrush LampAlarm = Brush("#f0584a");
    public static readonly IBrush LightWhite = Brush("#fff6d8");
    public static readonly IBrush LightRed = Brush("#ff3b30");
    public static readonly IBrush Command = Brush("#8b96a3");

    public static IBrush Of(SpeedJudgement judgement) => judgement switch
    {
        SpeedJudgement.Within => Within,
        SpeedJudgement.Slightly => Slightly,
        SpeedJudgement.Over => Over,
        SpeedJudgement.Autopilot => Autopilot,
        _ => NoData
    };

    /// <summary>What a signal shows: the colour of its main light, as the in-game map draws it.</summary>
    public static IBrush Of(AspectChange? aspect)
    {
        if (aspect is not { } a)
            return SignalUnknown;
        bool shunt = a.Command.StartsWith("Shunt", System.StringComparison.OrdinalIgnoreCase);
        if (a.Speed1 == 0)
            return shunt ? SignalShuntStop : SignalStop;
        if (shunt)
            return SignalShunt;
        return a.Speed1 < 0 && a.Speed2 != 0 ? SignalFree : SignalLimited;
    }

    /// <summary>A colour seen through: for fills under lines and glows.</summary>
    public static IBrush Faded(IBrush brush, double opacity) =>
        new SolidColorBrush(((ISolidColorBrush)brush).Color, opacity).ToImmutable();

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color)).ToImmutable();
}
