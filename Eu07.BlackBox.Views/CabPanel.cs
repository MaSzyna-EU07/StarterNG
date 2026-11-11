using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Eu07.BlackBox.Views;

/// <summary>
/// The cab of the user's train at the moment played, for checking what the driver did: the
/// speed against the AI's, the accelerations, the controllers, the brakes (the pressures as
/// gauges, the setting), the traction as fits what drives the vehicle (the voltage and the
/// current of an electric one, the oil and the temperatures of a diesel), the lights at the
/// head and the tail of the train with what they are to show, and a lamp for every device in
/// groups (power, driving, safety, doors), the alerter (CA) and the cab signalling (SHP) apart.
/// </summary>
public sealed class CabPanel : Control
{
    private const double Pad = 14;
    private const double LampRow = 14;
    private const int DoorColumns = 3;

    private Recording? _recording;
    private double _time;
    private int _measured = -1;
    private int _controllerTop = 1;
    private readonly HashSet<int> _powered = new();
    private readonly HashSet<int> _feedPipe = new();
    private readonly HashSet<int> _integrated = new();
    private readonly HashSet<int> _hydraulic = new();

    public CabPanel()
    {
        ClipToBounds = true;
        Height = 650;
    }

    public IBlackBoxStrings Strings { get; set; } = new DefaultBlackBoxStrings();

    public Recording? Recording
    {
        get => _recording;
        set
        {
            _recording = value;
            _measured = -1;
            InvalidateVisual();
        }
    }

    public double Time
    {
        get => _time;
        set
        {
            _time = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        double width = size.Width - 2 * Pad, right = size.Width - Pad;

        // The last sample, unless the train was out of sight for longer than a moment.
        if (_recording is not { } recording || recording.CabAt(_time) is not { } s || _time - s.Time > 1.5)
        {
            context.DrawText(MapView.Text(Strings["CabNoData"], Palette.DimText, 12), new Point(Pad, 40));
            return;
        }
        Measure(recording);
        var culture = CultureInfo.CurrentCulture;
        var extra = s.Extra;

        // The vehicle at the left; who drives, from which cab, at the right.
        double chipX = right;
        if (s.CabEnd != 0)
            chipX -= ChipLeft(context, Strings[s.CabEnd > 0 ? "CabEndFront" : "CabEndRear"], Palette.Text, chipX, 4) + 5;
        var (driverText, driverBrush) = !s.InCab ? (Strings["CabOutside"], Palette.DimText)
            : s.Driving == Driver.Ai ? (Strings["CabDriverAi"], Palette.Autopilot)
            : (Strings["CabDriverUser"], Palette.UserTrain);
        chipX -= ChipLeft(context, driverText, driverBrush, chipX, 4) + 10;
        using (context.PushClip(new Rect(Pad, 0, Math.Max(0, chipX - Pad), 28)))
            context.DrawText(MapView.Text(recording.Vehicle(s.Vehicle)?.Name ?? "", Palette.Text, 12, FontWeight.SemiBold), new Point(Pad, 6));

        // The speed, large, what the AI would do under it, the accelerations at the right.
        var judgement = DrivingSummary.Judge(s);
        var speed = MapView.Text(s.AbsSpeedKmh.ToString("0", culture), judgement is SpeedJudgement.Over ? Palette.Bad
                                     : judgement is SpeedJudgement.Slightly ? Palette.Warn : Palette.Text, 36, FontWeight.SemiBold);
        context.DrawText(speed, new Point(Pad, 24));
        context.DrawText(MapView.Text("km/h", Palette.DimText, 12), new Point(Pad + speed.Width + 4, 24 + speed.Height - 21));
        double gaugeX = Pad + Math.Max(150, width * 0.42);
        if (s.AiValid)
        {
            using (context.PushClip(new Rect(Pad, 76, gaugeX - Pad - 8, 40)))
            {
                context.DrawText(MapView.Text(string.Format(culture, Strings["CabAiTarget"], s.Ai.TargetKmh), Palette.Target, 12), new Point(Pad, 76));
                if (s.Ai.HasNextLimit)
                    context.DrawText(MapView.Text(string.Format(culture, Strings["LimitAt"], s.Ai.NextLimitKmh, s.Ai.NextLimitDistance), Palette.Limit, 11),
                                     new Point(Pad, 93));
            }
        }
        double gaugeWidth = right - gaugeX;
        Balance(context, Strings["CabAcceleration"], s.Acceleration, 1.5, gaugeX, 34, gaugeWidth);
        Balance(context, Strings["CabLateral"], s.LateralAcceleration, 1.5, gaugeX, 70, gaugeWidth);

        // The controllers.
        double top = Section(context, Strings["CabSectionControls"], Pad, 118, right);
        double column = width / 4;
        Value(context, Strings["CabController"], s.MainController.ToString(culture), Pad, top);
        Steps(context, s.MainController, _controllerTop, Pad, top + 34, column - 14);
        if (extra is { } handle && _integrated.Contains(s.Vehicle))
            Value(context, Strings["CabIntegrated"], (handle.IntegratedController * 100).ToString("+0;−0;0", culture) + " %", Pad + column, top);
        else
            Value(context, Strings["CabShunt"], s.ScndController.ToString(culture), Pad + column, top);
        Value(context, Strings["CabReverser"], Strings[s.Reverser > 0 ? "CabForward" : s.Reverser < 0 ? "CabBackward" : "CabNeutral"], Pad + 2 * column, top);
        Value(context, Strings["CabTrainBrake"], s.TrainBrake.ToString("0.0", culture), Pad + 3 * column, top);

        // The brakes, as gauges, the setting (G, P, R) at the right of the title.
        string? setting = extra is { } e && (e.BrakeDelay | e.BrakeMode) != 0
            ? string.Format(culture, Strings["CabBrakeSetting"], string.Join(" · ", new[] { BrakeSetting(e.BrakeDelay), BrakeMode(e.BrakeMode) }.Where(t => t.Length > 0)))
            : null;
        top = Section(context, Strings["CabSectionBrakes"], Pad, 180, right, setting);
        const double gauge = 17;
        Gauge(context, Strings["CabPipe"], s.BrakePipe, 6, 5, $"{s.BrakePipe:0.00} bar", Palette.Station, Pad, top, width);
        double y = top + gauge;
        if (extra is { } x1)
        {
            // where the driver's brake valve takes the pipe
            Gauge(context, Strings["CabEqualizing"], x1.EqualizingReservoir, 6, null, $"{x1.EqualizingReservoir:0.00} bar", Palette.Target, Pad, y, width);
            y += gauge;
        }
        Gauge(context, Strings["CabCylinder"], s.BrakeCylinder, 6, null, $"{s.BrakeCylinder:0.0} bar", Palette.Brakes, Pad, y, width);
        y += gauge;
        if (extra is { } x3)
        {
            // the most braked vehicle of the train
            Gauge(context, Strings["CabConsistCylinder"], x3.ConsistBrakeCylinder, 6, null, $"{x3.ConsistBrakeCylinder:0.0} bar", Palette.Brakes, Pad, y, width);
            y += gauge;
        }
        Gauge(context, Strings["CabReservoir"], s.MainReservoir, 10, null, $"{s.MainReservoir:0.0} bar", Palette.Controller, Pad, y, width);
        y += gauge;
        if (extra is { } x2)
        {
            bool has = _feedPipe.Contains(s.Vehicle);
            Gauge(context, Strings["CabFeedPipe"], has ? x2.FeedPipe : 0, 10, null, has ? $"{x2.FeedPipe:0.0} bar" : "—", Palette.Controller, Pad, y, width);
            y += gauge;
        }
        Gauge(context, Strings["CabLocalBrake"], s.LocalBrake, 1, null, $"{s.LocalBrake * 100:0} %", Palette.Brakes, Pad, y, width);

        // The traction at the left, as fits what drives the vehicle; the lights of both ends of the train at the right.
        double half = Pad + width / 2;
        double driveTop = y + gauge + 4;
        top = Section(context, Strings["CabSectionDrive"], Pad, driveTop, half - 10);
        Section(context, Strings["CabLights"], half + 4, driveTop, right);
        DrawDrive(context, s, top, (half - 10 - Pad) / 3, culture);
        double row = top;
        foreach (var (key, lights, equipped, required, isRight) in new[]
                 {
                     ("CabLightsHead", s.LightsHead, s.EquippedHead, HeadCodes.RequiredHead(s), HeadCodes.HeadRight(s)),
                     ("CabLightsTail", s.LightsTail, s.EquippedTail, HeadCodes.RequiredTail(s), HeadCodes.TailRight(s))
                 })
        {
            DrawFace(context, lights, new Point(half + 4 + 17, row + 15));
            // the end, and under it the signal it shows: green as it is to be (with the lamps the vehicle has),
            // red otherwise with the one it is to show
            var shown = HeadCodes.Of(lights);
            var text = HeadCodes.NoLamps(equipped) ? Strings["CabLightsNone"]
                : isRight == false && required is { } want
                ? string.Format(culture, Strings["CabLightsWant"], HeadCodes.Name(shown), HeadCodes.Name(want))
                : isRight == true && required is { } met ? HeadCodes.Name(met) : HeadCodes.Name(shown);
            var brush = isRight is null ? Palette.DimText : isRight.Value ? Palette.Good : Palette.Bad;
            using (context.PushClip(new Rect(half + 44, row, right - half - 44, 28)))
            {
                context.DrawText(MapView.Text(Strings[key], Palette.DimText, 10), new Point(half + 46, row));
                context.DrawText(MapView.Text(text, brush, 12, FontWeight.SemiBold), new Point(half + 46, row + 11));
            }
            row += 27;
        }

        // The devices, in three groups side by side: lit as their lamps are in the cab, the ones off faint.
        top = Section(context, Strings["CabSectionDevices"], Pad, top + 76, right);
        var groups = Devices(s, culture);
        var widths = new double[groups.Length];
        for (int g = 0; g < groups.Length; g++)
        {
            widths[g] = MapView.Text(Strings[groups[g].Title], Palette.DimText, 10, FontWeight.SemiBold).Width;
            foreach (var (text, _) in groups[g].Lamps)
                widths[g] = Math.Max(widths[g], 14 + MapView.Text(text, Palette.Text, 10.5).Width);
        }
        // each group as wide as its longest name, what is left shared between them
        double spare = (width - widths.Sum()) / groups.Length;
        double x = Pad, bottom = top;
        for (int g = 0; g < groups.Length; g++)
        {
            double groupWidth = Math.Max(20, widths[g] + spare);
            using (context.PushClip(new Rect(x, top, groupWidth, size.Height - top)))
            {
                context.DrawText(MapView.Text(Strings[groups[g].Title], Palette.DimText, 10, FontWeight.SemiBold), new Point(x, top));
                double lampY = top + 16;
                foreach (var (text, brush) in groups[g].Lamps)
                {
                    Lamp(context, text, brush, x, lampY);
                    lampY += LampRow;
                }
                bottom = Math.Max(bottom, lampY);
            }
            x += groupWidth;
        }

        // The doors under them, across: open, permitted, the departure signal, the steps and the lock.
        top = Math.Max(bottom, top + 16 + 9 * LampRow) + 6;
        context.DrawText(MapView.Text(Strings["CabGroupDoors"], Palette.DimText, 10, FontWeight.SemiBold), new Point(Pad, top));
        var doors = Doors(s);
        double doorColumn = width / DoorColumns;
        for (int i = 0; i < doors.Length; i++)
        {
            double doorX = Pad + i % DoorColumns * doorColumn;
            using (context.PushClip(new Rect(doorX, top, doorColumn, size.Height - top)))
                Lamp(context, doors[i].Text, doors[i].Lit, doorX, top + 16 + i / DoorColumns * LampRow);
        }
    }

    /// <summary>
    /// The traction, in two rows of three, as fits what drives the vehicle: the voltage, the current and the
    /// revolutions of an electric one, the power and the cruise control under; the revolutions and the oil
    /// pressure of a diesel, its temperatures under. A recording made before the engine was written: the first
    /// row of an electric one, no voltage for a vehicle which never had any.
    /// </summary>
    private void DrawDrive(DrawingContext context, CabSample s, double top, double third, CultureInfo culture)
    {
        void At(int column, int row, string label, string value, IBrush? brush = null) =>
            Value(context, label, value, Pad + column * third, top + row * 36, brush);

        string voltage = _powered.Contains(s.Vehicle) ? $"{s.Voltage:0} V" : "—";
        if (s.Extra is not { } e)
        {
            At(0, 0, Strings["CabVoltage"], voltage);
            At(1, 0, Strings["CabCurrent"], $"{s.MotorCurrent:0} A");
            At(2, 0, Strings["CabRpm"], $"{s.EngineRpm:0}");
            return;
        }
        string oil = string.Format(culture, "{0:0.00} MPa", e.OilPressure);
        switch (e.Engine)
        {
            case EngineKind.Electric:
            case EngineKind.ElectricInduction:
                At(0, 0, Strings["CabVoltage"], voltage);
                At(1, 0, Strings["CabCurrent"], $"{s.MotorCurrent:0} A");
                At(2, 0, Strings["CabRpm"], $"{s.EngineRpm:0}");
                At(0, 1, Strings["CabPower"], $"{e.EnginePower:0} kW");
                At(1, 1, Strings["CabSpeedControl"], s.Has(CabDevices.SpeedControl) ? $"{e.SpeedControlKmh:0} km/h" : "—");
                break;
            case EngineKind.DieselElectric:
            case EngineKind.Diesel:
                At(0, 0, Strings["CabRpm"], $"{s.EngineRpm:0}");
                if (e.Engine == EngineKind.DieselElectric)
                    At(1, 0, Strings["CabCurrent"], $"{s.MotorCurrent:0} A");
                else if (_hydraulic.Contains(s.Vehicle))
                    At(1, 0, Strings["CabConverterFill"], $"{e.ConverterFill * 100:0} %");
                else
                    At(1, 0, Strings["CabClutch"], $"{e.Clutch * 100:0} %");
                At(2, 0, Strings["CabOilPressure"], oil);
                // the temperatures, warm when the engine is too hot
                var heat = s.Has(CabDevices.EngineHot) ? Palette.Bad : null;
                At(0, 1, Strings["CabWater"], $"{e.WaterTemperature:0} °C", heat);
                At(1, 1, Strings["CabOil"], $"{e.OilTemperature:0} °C", heat);
                At(2, 1, Strings["CabEngine"], $"{e.EngineTemperature:0} °C", heat);
                break;
            case EngineKind.Steam:
                At(0, 0, Strings["CabRpm"], $"{s.EngineRpm:0}");
                break;
            default:
                context.DrawText(MapView.Text(Strings["CabNoDrive"], Palette.DimText, 11), new Point(Pad, top + 2));
                break;
        }
    }

    /// <summary>The setting of the brakes from its bits: G, P, R, R+Mg.</summary>
    public static string BrakeSetting(byte bits)
    {
        var parts = new List<string>();
        if ((bits & 1) != 0)
            parts.Add("G");
        if ((bits & 2) != 0)
            parts.Add("P");
        if ((bits & 4) != 0)
            parts.Add("R");
        if ((bits & 8) != 0)
            parts.Add("Mg");
        return string.Join("+", parts);
    }

    /// <summary>The mode of the brakes from its bits: PS, PN, EP, MED.</summary>
    public static string BrakeMode(byte bits)
    {
        var parts = new List<string>();
        if ((bits & 1) != 0)
            parts.Add("PS");
        if ((bits & 2) != 0)
            parts.Add("PN");
        if ((bits & 4) != 0)
            parts.Add("EP");
        if ((bits & 8) != 0)
            parts.Add("MED");
        return string.Join("+", parts);
    }

    /// <summary>The title of a part of the panel, with a line from it to the right (and a note at its end); the top of the part returned.</summary>
    private static double Section(DrawingContext context, string title, double x, double y, double right, string? note = null)
    {
        var text = MapView.Text(title.ToUpper(CultureInfo.CurrentCulture), Palette.DimText, 9, FontWeight.SemiBold);
        context.DrawText(text, new Point(x, y));
        double end = right;
        if (note is not null)
        {
            var noted = MapView.Text(note, Palette.Text, 10, FontWeight.SemiBold);
            end = right - noted.Width - 8;
            context.DrawText(noted, new Point(right - noted.Width, y + (text.Height - noted.Height) / 2));
        }
        double line = Math.Round(y + text.Height / 2) + 0.5;
        if (x + text.Width + 8 < end)
            context.DrawLine(new Pen(Palette.Grid, 1), new Point(x + text.Width + 8, line), new Point(end, line));
        return y + 17;
    }

    /// <summary>
    /// What is the same all the recording long for each vehicle: the highest step of the controller used, for
    /// its bar, and which vehicles ever had a voltage, a main reservoir pipe, an integrated controller moved off
    /// nought and a torque converter filled, to show the field shunting, the clutch or none for the others.
    /// </summary>
    private void Measure(Recording recording)
    {
        if (_measured == recording.Revision)
            return;
        int top = 1;
        _powered.Clear();
        _feedPipe.Clear();
        _integrated.Clear();
        _hydraulic.Clear();
        foreach (var sample in recording.Cab)
        {
            top = Math.Max(top, sample.MainController);
            if (sample.Voltage > 1)
                _powered.Add(sample.Vehicle);
            if (sample.Extra is not { } e)
                continue;
            if (e.FeedPipe > 0.1f)
                _feedPipe.Add(sample.Vehicle);
            if (Math.Abs(e.IntegratedController) > 0.01f)
                _integrated.Add(sample.Vehicle);
            if (e.ConverterFill > 0.01f)
                _hydraulic.Add(sample.Vehicle);
        }
        _controllerTop = top;
        _measured = recording.Revision;
    }

    private static void Value(DrawingContext context, string label, string value, double x, double y, IBrush? brush = null)
    {
        context.DrawText(MapView.Text(label, Palette.DimText, 10), new Point(x, y));
        context.DrawText(MapView.Text(value, brush ?? Palette.Text, 14, FontWeight.SemiBold), new Point(x, y + 13));
    }

    /// <summary>The steps of the controller as a row of cells, lit up to the one it stands at.</summary>
    private static void Steps(DrawingContext context, int step, int top, double x, double y, double width)
    {
        int cells = Math.Clamp(top, 1, 40);
        double cell = width / cells;
        for (int i = 0; i < cells; i++)
            context.DrawRectangle(i < step ? Palette.Controller : Palette.LampOff, null,
                                  new Rect(x + i * cell, y, Math.Max(1, cell - (cells > 20 ? 0.5 : 1.5)), 4), 1, 1);
    }

    /// <summary>A gauge: its name, a bar up to the value against the scale, a mark where it should stand, the value.</summary>
    private static void Gauge(DrawingContext context, string label, float value, double scale, double? mark, string text,
                              IBrush brush, double x, double y, double width)
    {
        const double labelWidth = 118, valueWidth = 64;
        context.DrawText(MapView.Text(label, Palette.DimText, 11), new Point(x, y));
        double barX = x + labelWidth, barWidth = width - labelWidth - valueWidth;
        var track = new Rect(barX, y + 5, barWidth, 6);
        context.DrawRectangle(Palette.LampOff, null, track, 3, 3);
        double filled = Math.Clamp(value / scale, 0, 1) * barWidth;
        if (filled > 0.5)
            context.DrawRectangle(brush, null, new Rect(barX, y + 5, filled, 6), 3, 3);
        if (mark is { } m)
        {
            double mx = barX + m / scale * barWidth;
            context.DrawLine(new Pen(Palette.Text, 1.5), new Point(mx, y + 2), new Point(mx, y + 14));
        }
        var v = MapView.Text(text, Palette.Text, 11, FontWeight.SemiBold);
        context.DrawText(v, new Point(x + width - v.Width, y));
    }

    /// <summary>A value either way of nought: a bar from the middle, to the right above it.</summary>
    private static void Balance(DrawingContext context, string label, float value, double scale, double x, double y, double width)
    {
        var culture = CultureInfo.CurrentCulture;
        context.DrawText(MapView.Text(label, Palette.DimText, 10), new Point(x, y));
        var v = MapView.Text($"{value.ToString("+0.00;−0.00;0.00", culture)} m/s²", Palette.Text, 11, FontWeight.SemiBold);
        context.DrawText(v, new Point(x + width - v.Width, y));
        double barY = y + 17, middle = x + width / 2;
        context.DrawRectangle(Palette.LampOff, null, new Rect(x, barY, width, 5), 2.5, 2.5);
        double length = Math.Clamp(value / scale, -1, 1) * width / 2;
        var brush = Math.Abs(value) > scale * 0.7 ? Palette.Bad : value >= 0 ? Palette.Good : Palette.Brakes;
        if (Math.Abs(length) > 0.5)
            context.DrawRectangle(brush, null, new Rect(Math.Min(middle, middle + length), barY, Math.Abs(length), 5), 2.5, 2.5);
        context.DrawLine(new Pen(Palette.DimText, 1), new Point(middle, barY - 2), new Point(middle, barY + 7));
    }

    /// <summary>A chip ending at x, its width returned.</summary>
    private static double ChipLeft(DrawingContext context, string text, IBrush brush, double right, double y)
    {
        var label = MapView.Text(text, brush, 11, FontWeight.SemiBold);
        var box = new Rect(right - label.Width - 12, y, label.Width + 12, label.Height + 4);
        context.DrawRectangle(Palette.Faded(brush, 0.14), null, box, 4, 4);
        context.DrawText(label, new Point(box.X + 6, y + 2));
        return box.Width;
    }

    /// <summary>The lights of an end as they sit on the front of a locomotive: one up in the middle, two below.</summary>
    private static void DrawFace(DrawingContext context, Lights lights, Point center)
    {
        context.DrawRectangle(null, new Pen(Palette.Grid, 1), new Rect(center.X - 17, center.Y - 15, 34, 22), 5, 5);
        bool rearEnd = lights.HasFlag(Lights.RearEndSignals);
        Light(context, center + new Vector(0, -8), lights.HasFlag(Lights.HeadlightUpper), false, false);
        Light(context, center + new Vector(-10, 1), lights.HasFlag(Lights.HeadlightLeft) || lights.HasFlag(Lights.AuxiliaryLeft),
              lights.HasFlag(Lights.RedMarkerLeft) || rearEnd, lights.HasFlag(Lights.HighBeamLeft));
        Light(context, center + new Vector(10, 1), lights.HasFlag(Lights.HeadlightRight) || lights.HasFlag(Lights.AuxiliaryRight),
              lights.HasFlag(Lights.RedMarkerRight) || rearEnd, lights.HasFlag(Lights.HighBeamRight));
    }

    private static void Light(DrawingContext context, Point center, bool white, bool red, bool high)
    {
        var fill = white ? Palette.LightWhite : red ? Palette.LightRed : Palette.LampOff;
        double radius = high ? 4.5 : 3.5;
        if (white || red)
            context.DrawEllipse(Palette.Faded(fill, 0.25), null, center, radius + 3, radius + 3);
        context.DrawEllipse(fill, null, center, radius, radius);
    }

    /// <summary>A device's lamp and its name: lit in its colour, the name bright; off, both faint.</summary>
    private static void Lamp(DrawingContext context, string text, IBrush? lit, double x, double y)
    {
        var center = new Point(x + 4, y + 7);
        if (lit is not null)
            context.DrawEllipse(Palette.Faded(lit, 0.3), null, center, 6, 6);
        context.DrawEllipse(lit ?? Palette.LampOff, null, center, 3.5, 3.5);
        context.DrawText(MapView.Text(text, lit is null ? Palette.FaintText : Palette.Text, 10.5), new Point(x + 14, y));
    }

    /// <summary>
    /// Every device but the doors, with the colour of its lamp if it was on, in the groups the panel shows. A
    /// diesel has its pumps where an electric vehicle has its pantographs; a recording made before the engine
    /// was written has the devices it had then.
    /// </summary>
    private (string Title, (string Text, IBrush? Lit)[] Lamps)[] Devices(CabSample s, CultureInfo culture)
    {
        (string, IBrush?) On(string key, CabDevices device, IBrush colour) => (Strings[key], s.Has(device) ? colour : null);
        (string, IBrush?) Flag(string key, CabFlags flag, IBrush colour) => (Strings[key], (s.Flags & flag) != 0 ? colour : null);
        IBrush? Security(SecurityFlags flag, IBrush colour) => s.Has(flag) ? colour : null;
        var extra = s.Extra;
        bool diesel = extra is { Engine: EngineKind.Diesel or EngineKind.DieselElectric };
        (string, IBrush?) Horn()
        {
            if (!s.Has(CabDevices.Horn))
                return (Strings["DevHorn"], null);
            string? tone = extra?.Horn switch
            {
                1 => Strings["HornLow"],
                2 => Strings["HornHigh"],
                3 => Strings["HornBoth"],
                _ => null
            };
            return (tone is null ? Strings["DevHorn"] : string.Format(culture, Strings["DevHornTone"], tone), Palette.LampWarning);
        }
        bool electric = extra is null or { Engine: EngineKind.Electric or EngineKind.ElectricInduction };

        var power = new List<(string, IBrush?)> { On("DevBattery", CabDevices.Battery, Palette.LampOn), On("DevMainBreaker", CabDevices.MainBreaker, Palette.LampOn) };
        if (electric)
        {
            power.Add(On("DevPantographFront", CabDevices.PantographFront, Palette.LampOn));
            power.Add(On("DevPantographRear", CabDevices.PantographRear, Palette.LampOn));
        }
        if (diesel)
        {
            power.Add(On("DevFuelPump", CabDevices.FuelPump, Palette.LampOn));
            power.Add(On("DevOilPump", CabDevices.OilPump, Palette.LampOn));
        }
        power.Add(On("DevConverter", CabDevices.Converter, Palette.LampOn));
        power.Add(On("DevCompressor", CabDevices.Compressor, Palette.LampOn));
        power.Add(On("DevLineContactors", CabDevices.LineContactors, Palette.LampOn));
        if (extra is not null)
            power.Add(On("DevMotorBlowers", CabDevices.MotorBlowers, Palette.LampOn));
        power.Add(On("DevHeating", CabDevices.Heating, Palette.LampOn));

        // the radio with its channel
        var radio = extra is { RadioChannel: > 0 } r
            ? (string.Format(culture, Strings["DevRadioChannel"], r.RadioChannel), s.Has(CabDevices.Radio) ? Palette.LampOn : null)
            : On("DevRadio", CabDevices.Radio, Palette.LampOn);
        var driving = new List<(string, IBrush?)>
        {
            On("DevShuntMode", CabDevices.ShuntMode, Palette.LampOn), radio,
            On("DevSanding", CabDevices.Sanding, Palette.LampWarning), Horn(),
            On("DevSpringBrake", CabDevices.SpringBrake, Palette.LampWarning)
        };
        if (extra is not null)
        {
            driving.Add((Strings["DevHandBrake"], extra.Value.HandBrake > 0 ? Palette.LampWarning : null));
            driving.Add(On("DevBrakeReleaser", CabDevices.BrakeReleaser, Palette.LampWarning));
            driving.Add(On("DevSpeedControl", CabDevices.SpeedControl, Palette.LampOn));
        }

        var safety = new List<(string, IBrush?)>();
        if (s.Security != SecurityFlags.None || s.CabEnd != 0)
        {
            // A recording which tells them apart.
            safety.Add((Strings["DevVigilance"], Security(SecurityFlags.VigilanceBeeping, Palette.LampAlarm) ?? Security(SecurityFlags.Vigilance, Palette.Vigilance)));
            safety.Add((Strings["DevCabSignal"], Security(SecurityFlags.CabSignalBeeping, Palette.LampAlarm) ?? Security(SecurityFlags.CabSignal, Palette.CabSignal)));
            safety.Add((Strings["DevSecurityBraking"], Security(SecurityFlags.Braking, Palette.LampAlarm)));
        }
        else
            safety.Add((Strings["DevAlerter"], (s.Flags & CabFlags.AlerterBeeping) != 0 ? Palette.LampAlarm
                                                 : (s.Flags & CabFlags.AlerterBlinking) != 0 ? Palette.LampWarning : null));
        safety.Add(Flag("DevWheelSlip", CabFlags.WheelSlip, Palette.LampAlarm));
        safety.Add(On("DevOverloadRelay", CabDevices.OverloadRelay, Palette.LampAlarm));
        safety.Add(Flag("DevRadioStop", CabFlags.RadioStop, Palette.LampAlarm));
        safety.Add(Flag("DevAlarmChain", CabFlags.AlarmChain, Palette.LampAlarm));
        if (diesel)
            safety.Add(On("DevEngineHot", CabDevices.EngineHot, Palette.LampAlarm));
        return [("CabGroupPower", power.ToArray()), ("CabGroupDriving", driving.ToArray()), ("CabGroupSafety", safety.ToArray())];
    }

    /// <summary>The doors of the vehicle the user is in, in rows of three: open, the lock, the permits, the steps, the departure signal.</summary>
    private (string Text, IBrush? Lit)[] Doors(CabSample s)
    {
        (string, IBrush?) On(string key, CabDevices device, IBrush colour) => (Strings[key], s.Has(device) ? colour : null);
        var doors = new List<(string, IBrush?)>
        {
            On("DevDoorsLeft", CabDevices.DoorsLeft, Palette.LampWarning), On("DevDoorsRight", CabDevices.DoorsRight, Palette.LampWarning)
        };
        if (s.Extra is null)
            return doors.ToArray();
        doors.Add(On("DevDoorLock", CabDevices.DoorLock, Palette.LampOn));
        doors.Add(On("DevDoorPermitLeft", CabDevices.DoorPermitLeft, Palette.LampOn));
        doors.Add(On("DevDoorPermitRight", CabDevices.DoorPermitRight, Palette.LampOn));
        doors.Add(On("DevDoorsLocked", CabDevices.DoorsLocked, Palette.LampOn));
        doors.Add(On("DevDoorStep", CabDevices.DoorStep, Palette.LampOn));
        doors.Add(On("DevDepartureSignal", CabDevices.DepartureSignal, Palette.LampWarning));
        return doors.ToArray();
    }
}
