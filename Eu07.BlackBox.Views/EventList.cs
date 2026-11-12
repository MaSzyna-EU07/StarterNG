using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Eu07.BlackBox.Views;

/// <summary>One line of the list of events.</summary>
public sealed record BlackBoxEvent(double Time, string Text, IBrush Brush, bool Important);

/// <summary>
/// What happened to the user's train, by the clock: the commands, the autopilot, the
/// signals passed, the stations, the horn points, the couplings, the train protection,
/// the cab and the camera of the user. A click goes to its moment.
/// </summary>
public sealed class EventList : ListBox
{
    private Recording? _recording;
    private int _shownRevision = -1;
    private List<BlackBoxEvent> _events = new();
    private bool _picking;

    public event Action<double>? Seek;

    // The look of a list box: a control of a type of its own has no theme otherwise, and shows nothing.
    protected override Type StyleKeyOverride => typeof(ListBox);

    public IBlackBoxStrings Strings { get; set; } = new DefaultBlackBoxStrings();

    public EventList()
    {
        SelectionMode = SelectionMode.Single;
        Padding = new Thickness(4, 0, 4, 6);
        // The picked line in the starter's green.
        Resources["ListBoxItemBackgroundSelected"] = Palette.AccentFill;
        Resources["ListBoxItemBackgroundSelectedPointerOver"] = Palette.AccentFillHover;
        Resources["ListBoxItemBackgroundSelectedPressed"] = Palette.AccentFill;
        Resources["ListBoxItemBackgroundPointerOver"] = Palette.CardHover;
        SelectionChanged += (_, _) =>
        {
            if (!_picking && SelectedIndex >= 0 && SelectedIndex < _events.Count)
                Seek?.Invoke(_events[SelectedIndex].Time);
        };
    }

    public Recording? Recording
    {
        get => _recording;
        set
        {
            _recording = value;
            _shownRevision = -1;
            Refresh();
        }
    }

    public IReadOnlyList<BlackBoxEvent> Events => _events;

    /// <summary>Takes in what the recording gained; the list grows at its end, so what is scrolled to stays.</summary>
    public void Refresh()
    {
        if (_recording is not { } recording)
        {
            _events.Clear();
            Items.Clear();
            return;
        }
        if (recording.Revision == _shownRevision)
            return;
        _shownRevision = recording.Revision;

        var events = Collect(recording, Strings);
        bool grown = events.Count >= _events.Count &&
                     _events.Select((e, i) => e == events[i]).All(same => same);
        if (!grown)
            Items.Clear();
        for (int i = grown ? _events.Count : 0; i < events.Count; i++)
            Items.Add(new ListBoxItem
            {
                Content = Line(recording, events[i]),
                Padding = new Thickness(8, 4),
                MinHeight = 0,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 1)
            });
        _events = events;
    }

    /// <summary>The event at or just before t picked, without going to it.</summary>
    public void ShowTime(double t)
    {
        int index = -1;
        for (int i = 0; i < _events.Count && _events[i].Time <= t + 0.05; i++)
            index = i;
        if (index == SelectedIndex)
            return;
        _picking = true;
        try
        {
            SelectedIndex = index;
            if (index >= 0)
                ScrollIntoView(index);
        }
        finally
        {
            _picking = false;
        }
    }

    private static Control Line(Recording recording, BlackBoxEvent e)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("58,14,*") };
        var time = new TextBlock
        {
            Text = recording.ClockAt(e.Time).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            Foreground = Palette.FaintText,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 0, 0)
        };
        var dot = new Border
        {
            Width = e.Important ? 8 : 6,
            Height = e.Important ? 8 : 6,
            CornerRadius = new CornerRadius(4),
            Background = e.Brush,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, e.Important ? 5 : 6, 0, 0)
        };
        var text = new TextBlock
        {
            Text = e.Text,
            Foreground = e.Important ? Palette.Text : Palette.DimText,
            FontSize = 12.5,
            FontWeight = e.Important ? FontWeight.SemiBold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(dot, 1);
        Grid.SetColumn(text, 2);
        grid.Children.Add(time);
        grid.Children.Add(dot);
        grid.Children.Add(text);
        return grid;
    }

    /// <summary>The events of the user's train, in the order they happened.</summary>
    public static List<BlackBoxEvent> Collect(Recording recording, IBlackBoxStrings strings)
    {
        var culture = CultureInfo.CurrentCulture;
        var events = new List<BlackBoxEvent>();
        foreach (var c in recording.UserCommands())
            events.Add(new BlackBoxEvent(c.Time, string.Format(culture, strings["EventCommand"], c.Name), Palette.Command, false));
        foreach (var d in recording.DriverChanges)
        {
            if (recording.CabAt(d.Time + 0.5) is not { } s || s.Vehicle != d.Vehicle)
                continue;
            events.Add(new BlackBoxEvent(d.Time, strings[d.Driving == Driver.Ai ? "EventAutopilotOn" : "EventAutopilotOff"],
                                         Palette.Autopilot, true));
        }
        foreach (var p in recording.UserPassings())
        {
            string name = recording.Signal(p.Signal)?.Name ?? p.Event;
            events.Add(p.AtDanger
                ? new BlackBoxEvent(p.Time, string.Format(culture, strings["EventDanger"], name, Math.Abs(p.SpeedKmh), p.Overrun), Palette.Over, true)
                : new BlackBoxEvent(p.Time, string.Format(culture, strings["EventPassed"], name, MapView.AspectText(strings, p.Speed1, p.Speed2),
                                                          Math.Abs(p.SpeedKmh)), Palette.Of(new AspectChange(p.Time, p.Signal, p.Speed1, p.Speed2, "")), false));
        }
        AddUserVehicles(recording, strings, events, culture);
        AddStations(recording, strings, events, culture);
        foreach (var horn in recording.UserHornChecks())
            events.Add(new BlackBoxEvent(horn.Request.Time, strings[horn.Sounded ? "EventHornSounded" : "EventHornMissed"],
                                         horn.Sounded ? Palette.HornPoint : Palette.Bad, !horn.Sounded));
        foreach (var span in recording.SecurityBrakings())
        {
            // Which of them was not acknowledged: the lamps blinking when it braked.
            string who = recording.CabAt(span.Start) is { } s
                ? s.Has(SecurityFlags.CabSignal) && s.Has(SecurityFlags.Vigilance) ? "CA/SHP" : s.Has(SecurityFlags.CabSignal) ? "SHP" : "CA"
                : "CA";
            events.Add(new BlackBoxEvent(span.Start, string.Format(culture, strings["EventSecurityBraking"], who), Palette.Bad, true));
        }
        AddCameras(recording, strings, events, culture);
        AddCouplings(recording, strings, events, culture);
        AddCabChanges(recording, strings, events, culture);
        foreach (var t in recording.Timetables)
            events.Add(new BlackBoxEvent(t.Time, string.Format(culture, strings["EventTimetable"], $"{t.Category} {t.Train}".Trim(),
                                                               string.Format(culture, strings["Relation"], t.From, t.To)), Palette.Station, false));
        if (recording.EndTime is { } end)
            events.Add(new BlackBoxEvent(end, strings["EventEnd"], Palette.FaintText, false));
        // Stable: what happened at the same moment stays in the order of its kind.
        return events.OrderBy(e => e.Time).ToList();
    }

    private static void AddUserVehicles(Recording recording, IBlackBoxStrings strings, List<BlackBoxEvent> events, CultureInfo culture)
    {
        UserVehicleChange? before = null;
        foreach (var u in recording.UserVehicles)
        {
            string vehicle = recording.Vehicle(u.Vehicle)?.Name ?? "";
            // Only the cab changed: told as such.
            if (before is { } b && b.Vehicle == u.Vehicle && b.InCab == u.InCab && b.CabEnd != u.CabEnd && u.CabEnd != 0)
                events.Add(new BlackBoxEvent(u.Time, string.Format(culture, strings["EventCab"], strings[u.CabEnd > 0 ? "CabEndFront" : "CabEndRear"], vehicle),
                                             Palette.UserTrain, true));
            else
            {
                string text = u.Vehicle == 0
                    ? strings["EventNoVehicle"]
                    : string.Format(culture, strings[u.InCab ? "EventInVehicle" : "EventOutside"], vehicle);
                events.Add(new BlackBoxEvent(u.Time, text, u.InCab ? Palette.UserTrain : Palette.DimText, true));
            }
            before = u;
        }
    }

    private static void AddStations(Recording recording, IBlackBoxStrings strings, List<BlackBoxEvent> events, CultureInfo culture)
    {
        foreach (var st in recording.UserStations())
        {
            switch (st.Kind)
            {
                case StationKind.Stopped:
                {
                    float error = st.StopError;
                    string where = Math.Abs(error) < 1
                        ? strings["TtStopExact"]
                        : string.Format(culture, strings[error > 0 ? "TtStopShort" : "TtStopPast"], Math.Abs(error));
                    events.Add(new BlackBoxEvent(st.Time, string.Format(culture, strings["EventStopped"], StationNames.Display(st.Station), where), Palette.Station, true));
                    break;
                }
                case StationKind.Passed:
                    events.Add(new BlackBoxEvent(st.Time, string.Format(culture, strings["EventStationPassed"], StationNames.Display(st.Station)), Palette.Station, false));
                    break;
                case StationKind.Missed:
                    events.Add(new BlackBoxEvent(st.Time, string.Format(culture, strings["EventStationMissed"], StationNames.Display(st.Station)), Palette.Bad, true));
                    break;
                case StationKind.Ready:
                    events.Add(new BlackBoxEvent(st.Time, string.Format(culture, strings["EventReady"], StationNames.Display(st.Station)), Palette.Station, false));
                    break;
            }
        }
    }

    /// <summary>The camera of the user going to another mode: the cab, outside, free.</summary>
    private static void AddCameras(Recording recording, IBlackBoxStrings strings, List<BlackBoxEvent> events, CultureInfo culture)
    {
        CameraMode? mode = null;
        foreach (var c in recording.Cameras)
        {
            if (c.Mode == mode)
                continue;
            if (mode is not null)
                events.Add(new BlackBoxEvent(c.Time, string.Format(culture, strings["EventCamera"], strings[CameraKey(c.Mode)]), Palette.DimText, false));
            mode = c.Mode;
        }
    }

    public static string CameraKey(CameraMode mode) => mode switch
    {
        CameraMode.Cab => "CameraCab",
        CameraMode.External => "CameraExternal",
        CameraMode.Debug => "CameraDebug",
        _ => "CameraFree"
    };

    /// <summary>
    /// The switches the driver worked, as the cab tells them: what came on and went off. Not the compressor and the
    /// motor fans, which go on and off by themselves.
    /// </summary>
    private static readonly (CabDevices Device, string Key)[] Switched =
    [
        (CabDevices.MainBreaker, "DevMainBreaker"), (CabDevices.PantographFront, "DevPantographFront"),
        (CabDevices.PantographRear, "DevPantographRear"), (CabDevices.FuelPump, "DevFuelPump"), (CabDevices.OilPump, "DevOilPump"),
        (CabDevices.Converter, "DevConverter"),
        (CabDevices.Heating, "DevHeating"), (CabDevices.Radio, "DevRadio"), (CabDevices.ShuntMode, "DevShuntMode"),
        (CabDevices.SpringBrake, "DevSpringBrake"), (CabDevices.BrakeReleaser, "DevBrakeReleaser"),
        (CabDevices.DoorPermitLeft, "DevDoorPermitLeft"), (CabDevices.DoorPermitRight, "DevDoorPermitRight"),
        (CabDevices.DoorStep, "DevDoorStep"), (CabDevices.DoorLock, "DevDoorLock"), (CabDevices.SpeedControl, "DevSpeedControl")
    ];

    /// <summary>
    /// What the driver did in the cab, from one look to the next in the same vehicle: the switches on and off, the
    /// doors opened and closed, the departure signal, the hand brake, the brakes' setting, the radio's channel, the
    /// speed the cruise control is set to; and, as faults, moving with the doors open and the engine overheating.
    /// </summary>
    private static void AddCabChanges(Recording recording, IBlackBoxStrings strings, List<BlackBoxEvent> events, CultureInfo culture)
    {
        CabSample? last = null;
        bool movingOpen = false;
        foreach (var s in recording.Cab)
        {
            if (last is not { } b || b.Vehicle != s.Vehicle)
            {
                last = s;
                movingOpen = false;
                continue;
            }
            bool Came(CabDevices device) => !b.Has(device) && s.Has(device);
            bool Went(CabDevices device) => b.Has(device) && !s.Has(device);
            foreach (var (device, key) in Switched)
            {
                if (Came(device))
                    events.Add(new BlackBoxEvent(s.Time, string.Format(culture, strings["EventDeviceOn"], strings[key]), Palette.LampOn, false));
                else if (Went(device))
                    events.Add(new BlackBoxEvent(s.Time, string.Format(culture, strings["EventDeviceOff"], strings[key]), Palette.FaintText, false));
            }
            foreach (var (device, open, closed) in new[] { (CabDevices.DoorsLeft, "DevDoorsLeft", "EventDoorsLeftClosed"), (CabDevices.DoorsRight, "DevDoorsRight", "EventDoorsRightClosed") })
            {
                if (Came(device))
                    events.Add(new BlackBoxEvent(s.Time, strings[open], Palette.LampWarning, false));
                else if (Went(device))
                    events.Add(new BlackBoxEvent(s.Time, strings[closed], Palette.FaintText, false));
            }
            if (Came(CabDevices.DepartureSignal))
                events.Add(new BlackBoxEvent(s.Time, strings["DevDepartureSignal"], Palette.LampWarning, false));
            if (Came(CabDevices.EngineHot))
                events.Add(new BlackBoxEvent(s.Time, strings["DevEngineHot"], Palette.Bad, true));
            // moving with the doors open: once, until they are closed or the train stands
            bool doorsOpen = s.Has(CabDevices.DoorsLeft) || s.Has(CabDevices.DoorsRight);
            if (doorsOpen && s.AbsSpeedKmh > 2 && !movingOpen)
            {
                events.Add(new BlackBoxEvent(s.Time, string.Format(culture, strings["EventMovingDoorsOpen"], s.AbsSpeedKmh), Palette.Bad, true));
                movingOpen = true;
            }
            else if (!doorsOpen || s.AbsSpeedKmh < 0.5)
                movingOpen = false;
            if (b.Extra is { } x && s.Extra is { } y)
            {
                if (x.HandBrake <= 0 && y.HandBrake > 0)
                    events.Add(new BlackBoxEvent(s.Time, strings["EventHandBrakeOn"], Palette.LampWarning, false));
                else if (x.HandBrake > 0 && y.HandBrake <= 0)
                    events.Add(new BlackBoxEvent(s.Time, strings["EventHandBrakeOff"], Palette.FaintText, false));
                if (x.BrakeDelay != y.BrakeDelay || x.BrakeMode != y.BrakeMode)
                {
                    string setting = string.Join(" · ", new[] { CabPanel.BrakeSetting(y.BrakeDelay), CabPanel.BrakeMode(y.BrakeMode) }.Where(t => t.Length > 0));
                    events.Add(new BlackBoxEvent(s.Time, string.Format(culture, strings["EventBrakeSetting"], setting), Palette.Brakes, false));
                }
                if (x.RadioChannel != y.RadioChannel && y.RadioChannel > 0)
                    events.Add(new BlackBoxEvent(s.Time, string.Format(culture, strings["EventRadioChannel"], y.RadioChannel), Palette.LampOn, false));
                // the speed set, while the cruise control holds one
                if (s.Has(CabDevices.SpeedControl) && (Came(CabDevices.SpeedControl) || Math.Abs(x.SpeedControlKmh - y.SpeedControlKmh) >= 1))
                    events.Add(new BlackBoxEvent(s.Time, string.Format(culture, strings["EventSpeedControlSet"], y.SpeedControlKmh), Palette.Target, false));
            }
            last = s;
        }
    }

    private static void AddCouplings(Recording recording, IBlackBoxStrings strings, List<BlackBoxEvent> events, CultureInfo culture)
    {
        string Name(int id) => recording.Vehicle(id)?.Name ?? id.ToString(culture);
        foreach (var c in recording.UserCouplings())
        {
            if (c.Coupled)
                events.Add(new BlackBoxEvent(c.Time, string.Format(culture, strings["EventCoupled"], Name(c.Vehicle), Name(c.Other)), Palette.Good, true));
            else if (c.Uncoupled)
                events.Add(new BlackBoxEvent(c.Time, string.Format(culture, strings["EventUncoupled"], Name(c.Vehicle), Name(c.PreviousOther)), Palette.Warn, true));
            else
                events.Add(new BlackBoxEvent(c.Time, string.Format(culture, strings["EventCouplingChanged"], Name(c.Vehicle), Name(c.Other),
                                                                   CouplingText(strings, c.Flags)), Palette.DimText, false));
        }
    }

    /// <summary>What a coupling joins, in words.</summary>
    public static string CouplingText(IBlackBoxStrings strings, CouplingFlags flags)
    {
        var parts = new List<string>();
        foreach (var (flag, key) in new[]
                 {
                     (CouplingFlags.Coupler, "CouplingCoupler"), (CouplingFlags.BrakePipe, "CouplingBrakePipe"),
                     (CouplingFlags.MainPipe, "CouplingMainPipe"), (CouplingFlags.Control, "CouplingControl"),
                     (CouplingFlags.Power, "CouplingPower"), (CouplingFlags.Heating, "CouplingHeating"),
                     (CouplingFlags.Gangway, "CouplingGangway")
                 })
            if ((flags & flag) != 0)
                parts.Add(strings[key]);
        return parts.Count == 0 ? strings["CouplingNothing"] : string.Join(", ", parts);
    }
}
