using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Eu07.BlackBox.Views;

/// <summary>
/// The cab of the user's train at the moment played: the speed against the AI's, the
/// accelerations, the controllers, the pressures of the brakes as gauges, the traction,
/// the lights at the head and the tail of the train, and a lamp for every device, the
/// alerter (CA) and the cab signalling (SHP) apart.
/// </summary>
public sealed class CabPanel : Control
{
    private const double Pad = 14;
    private const double LampRow = 16;
    private const int LampColumns = 3;

    private Recording? _recording;
    private double _time;
    private int _measured = -1;
    private int _controllerTop = 1;

    public CabPanel()
    {
        ClipToBounds = true;
        Height = 428;
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
        double width = size.Width - 2 * Pad;

        // The last sample, unless the train was out of sight for longer than a moment.
        if (_recording is not { } recording || recording.CabAt(_time) is not { } s || _time - s.Time > 1.5)
        {
            context.DrawText(MapView.Text(Strings["CabNoData"], Palette.DimText, 12), new Point(Pad, 40));
            return;
        }
        Measure(recording);
        var culture = CultureInfo.CurrentCulture;

        // Who drives, from which cab of which vehicle.
        double chipX = size.Width - Pad;
        string name = recording.Vehicle(s.Vehicle)?.Name ?? "";
        if (s.CabEnd != 0)
            chipX -= ChipLeft(context, Strings[s.CabEnd > 0 ? "CabEndFront" : "CabEndRear"], Palette.Text, chipX, 10) + 5;
        var (driverText, driverBrush) = !s.InCab ? (Strings["CabOutside"], Palette.DimText)
            : s.Driving == Driver.Ai ? (Strings["CabDriverAi"], Palette.Autopilot)
            : (Strings["CabDriverUser"], Palette.UserTrain);
        chipX -= ChipLeft(context, driverText, driverBrush, chipX, 10) + 8;
        var vehicle = MapView.Text(name, Palette.FaintText, 11);
        if (chipX - vehicle.Width > Pad + 70)
            context.DrawText(vehicle, new Point(chipX - vehicle.Width, 13));

        // The speed, large, and what the AI would do.
        double top = 38;
        var judgement = DrivingSummary.Judge(s);
        var speed = MapView.Text(s.AbsSpeedKmh.ToString("0", culture), judgement is SpeedJudgement.Over ? Palette.Bad
                                     : judgement is SpeedJudgement.Slightly ? Palette.Warn : Palette.Text, 38, FontWeight.SemiBold);
        context.DrawText(speed, new Point(Pad, top - 4));
        context.DrawText(MapView.Text("km/h", Palette.DimText, 12), new Point(Pad + speed.Width + 4, top + speed.Height - 24));
        if (s.AiValid)
        {
            context.DrawText(MapView.Text(string.Format(culture, Strings["CabAiTarget"], s.Ai.TargetKmh), Palette.Target, 12),
                             new Point(Pad, top + speed.Height - 4));
            if (s.Ai.HasNextLimit)
                context.DrawText(MapView.Text(string.Format(culture, Strings["LimitAt"], s.Ai.NextLimitKmh, s.Ai.NextLimitDistance), Palette.Limit, 11),
                                 new Point(Pad, top + speed.Height + 12));
        }

        // The accelerations, as needles from the middle: along the track and across it.
        double gaugeX = Pad + Math.Max(150, width * 0.42);
        double gaugeWidth = size.Width - Pad - gaugeX;
        Balance(context, Strings["CabAcceleration"], s.Acceleration, 1.5, gaugeX, top + 2, gaugeWidth);
        Balance(context, Strings["CabLateral"], s.LateralAcceleration, 1.5, gaugeX, top + 34, gaugeWidth);

        // The controllers.
        top = 112;
        Divider(context, top - 8, size.Width);
        double column = width / 4;
        Value(context, Strings["CabController"], s.MainController.ToString(culture), Pad, top);
        Steps(context, s.MainController, _controllerTop, Pad, top + 34, column - 14);
        Value(context, Strings["CabShunt"], s.ScndController.ToString(culture), Pad + column, top);
        Value(context, Strings["CabReverser"], Strings[s.Reverser > 0 ? "CabForward" : s.Reverser < 0 ? "CabBackward" : "CabNeutral"], Pad + 2 * column, top);
        Value(context, Strings["CabTrainBrake"], s.TrainBrake.ToString("0.0", culture), Pad + 3 * column, top);

        // The brakes, as gauges.
        top = 160;
        Divider(context, top - 6, size.Width);
        Gauge(context, Strings["CabPipe"], s.BrakePipe, 6, 5, $"{s.BrakePipe:0.00} bar", Palette.Station, Pad, top, width);
        Gauge(context, Strings["CabCylinder"], s.BrakeCylinder, 6, null, $"{s.BrakeCylinder:0.0} bar", Palette.Brakes, Pad, top + 18, width);
        Gauge(context, Strings["CabReservoir"], s.MainReservoir, 10, null, $"{s.MainReservoir:0.0} bar", Palette.Controller, Pad, top + 36, width);
        Gauge(context, Strings["CabLocalBrake"], s.LocalBrake, 1, null, $"{s.LocalBrake * 100:0} %", Palette.Brakes, Pad, top + 54, width);

        // The traction and the lights at both ends of the train.
        top = 242;
        Divider(context, top - 6, size.Width);
        double third = width * 0.62 / 3;
        Value(context, Strings["CabVoltage"], $"{s.Voltage:0} V", Pad, top);
        Value(context, Strings["CabCurrent"], $"{s.MotorCurrent:0} A", Pad + third, top);
        Value(context, Strings["CabRpm"], $"{s.EngineRpm:0}", Pad + 2 * third, top);
        double lightsX = Pad + 3 * third + 4;
        context.DrawText(MapView.Text(Strings["CabLights"], Palette.DimText, 10), new Point(lightsX, top));
        double faceX = lightsX;
        foreach (var (key, lights, required) in new[]
                 {
                     ("CabLightsHead", s.LightsHead, HeadCodes.RequiredHead(s)),
                     ("CabLightsTail", s.LightsTail, HeadCodes.RequiredTail(s))
                 })
        {
            DrawFace(context, lights, new Point(faceX + 18, top + 24));
            // the end, and the signal it shows: green as it is to be, red otherwise with the one it is to show
            var shown = HeadCodes.Of(lights);
            var text = required is { } want && want != shown
                ? $"{HeadCodes.Name(shown)} ≠ {HeadCodes.Name(want)}"
                : HeadCodes.Name(shown);
            var brush = required is null ? Palette.FaintText : required == shown ? Palette.Good : Palette.Bad;
            var label = MapView.Text($"{Strings[key]} {text}", brush, 9);
            context.DrawText(label, new Point(faceX + 18 - label.Width / 2, top + 34));
            faceX += 62;
        }

        // The lamps.
        top = 292;
        Divider(context, top - 6, size.Width);
        int i = 0;
        double lampColumn = width / LampColumns;
        foreach (var (key, brush) in Lamps(s))
        {
            var at = new Point(Pad + i % LampColumns * lampColumn, top + i / LampColumns * LampRow);
            Led(context, at + new Vector(5, 7), brush);
            context.DrawText(MapView.Text(Strings[key], brush is null ? Palette.FaintText : Palette.Text, 10.5), at + new Vector(14, 0));
            i++;
        }
    }

    /// <summary>The highest step of the controller used in the recording, for its bar.</summary>
    private void Measure(Recording recording)
    {
        if (_measured == recording.Revision)
            return;
        int top = 1;
        foreach (var sample in recording.Cab)
            top = Math.Max(top, sample.MainController);
        _controllerTop = top;
        _measured = recording.Revision;
    }

    private static void Divider(DrawingContext context, double y, double width) =>
        context.DrawLine(new Pen(Palette.Grid, 1), new Point(Pad, y + 0.5), new Point(width - Pad, y + 0.5));

    private static void Value(DrawingContext context, string label, string value, double x, double y)
    {
        context.DrawText(MapView.Text(label, Palette.DimText, 10), new Point(x, y));
        context.DrawText(MapView.Text(value, Palette.Text, 14, FontWeight.SemiBold), new Point(x, y + 13));
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
        Lamp(context, center + new Vector(0, -8), lights.HasFlag(Lights.HeadlightUpper), false, false);
        Lamp(context, center + new Vector(-10, 1), lights.HasFlag(Lights.HeadlightLeft) || lights.HasFlag(Lights.AuxiliaryLeft),
             lights.HasFlag(Lights.RedMarkerLeft) || rearEnd, lights.HasFlag(Lights.HighBeamLeft));
        Lamp(context, center + new Vector(10, 1), lights.HasFlag(Lights.HeadlightRight) || lights.HasFlag(Lights.AuxiliaryRight),
             lights.HasFlag(Lights.RedMarkerRight) || rearEnd, lights.HasFlag(Lights.HighBeamRight));
    }

    private static void Lamp(DrawingContext context, Point center, bool white, bool red, bool high)
    {
        var fill = white ? Palette.LightWhite : red ? Palette.LightRed : Palette.LampOff;
        double radius = high ? 4.5 : 3.5;
        if (white || red)
            context.DrawEllipse(Palette.Faded(fill, 0.25), null, center, radius + 3, radius + 3);
        context.DrawEllipse(fill, null, center, radius, radius);
    }

    private static void Led(DrawingContext context, Point center, IBrush? lit)
    {
        if (lit is not null)
            context.DrawEllipse(Palette.Faded(lit, 0.3), null, center, 6, 6);
        context.DrawEllipse(lit ?? Palette.LampOff, null, center, 3.5, 3.5);
    }

    /// <summary>Every device, with the colour of its lamp if it was on.</summary>
    private static IEnumerable<(string Key, IBrush? Lit)> Lamps(CabSample s)
    {
        IBrush? On(CabDevices device, IBrush colour) => s.Has(device) ? colour : null;
        IBrush? Flag(CabFlags flag, IBrush colour) => (s.Flags & flag) != 0 ? colour : null;
        IBrush? Security(SecurityFlags flag, IBrush colour) => s.Has(flag) ? colour : null;

        yield return ("DevBattery", On(CabDevices.Battery, Palette.LampOn));
        yield return ("DevMainBreaker", On(CabDevices.MainBreaker, Palette.LampOn));
        yield return ("DevConverter", On(CabDevices.Converter, Palette.LampOn));
        yield return ("DevPantographFront", On(CabDevices.PantographFront, Palette.LampOn));
        yield return ("DevPantographRear", On(CabDevices.PantographRear, Palette.LampOn));
        yield return ("DevCompressor", On(CabDevices.Compressor, Palette.LampOn));
        yield return ("DevLineContactors", On(CabDevices.LineContactors, Palette.LampOn));
        yield return ("DevHeating", On(CabDevices.Heating, Palette.LampOn));
        yield return ("DevRadio", On(CabDevices.Radio, Palette.LampOn));
        yield return ("DevShuntMode", On(CabDevices.ShuntMode, Palette.LampOn));
        yield return ("DevDoorsLeft", On(CabDevices.DoorsLeft, Palette.LampWarning));
        yield return ("DevDoorsRight", On(CabDevices.DoorsRight, Palette.LampWarning));
        yield return ("DevSanding", On(CabDevices.Sanding, Palette.LampWarning));
        yield return ("DevHorn", On(CabDevices.Horn, Palette.LampWarning));
        yield return ("DevSpringBrake", On(CabDevices.SpringBrake, Palette.LampWarning));
        if (s.Security != SecurityFlags.None || s.CabEnd != 0)
        {
            // A recording which tells them apart.
            yield return ("DevVigilance", Security(SecurityFlags.VigilanceBeeping, Palette.LampAlarm) ?? Security(SecurityFlags.Vigilance, Palette.Vigilance));
            yield return ("DevCabSignal", Security(SecurityFlags.CabSignalBeeping, Palette.LampAlarm) ?? Security(SecurityFlags.CabSignal, Palette.CabSignal));
            yield return ("DevSecurityBraking", Security(SecurityFlags.Braking, Palette.LampAlarm));
        }
        else
            yield return ("DevAlerter", Flag(CabFlags.AlerterBeeping, Palette.LampAlarm) ?? Flag(CabFlags.AlerterBlinking, Palette.LampWarning));
        yield return ("DevWheelSlip", Flag(CabFlags.WheelSlip, Palette.LampAlarm));
        yield return ("DevOverloadRelay", On(CabDevices.OverloadRelay, Palette.LampAlarm));
        yield return ("DevRadioStop", Flag(CabFlags.RadioStop, Palette.LampAlarm));
        yield return ("DevAlarmChain", Flag(CabFlags.AlarmChain, Palette.LampAlarm));
    }
}
