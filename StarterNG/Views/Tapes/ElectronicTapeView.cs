using System;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using StarterNG.Domain.Tapes;

namespace StarterNG.Views.Tapes;

/// <summary>
/// A trip on the screen of an electronic recorder - no paper, no stylus, no
/// clockwork: the speed over a labelled km/h axis, the brake pipe and cylinder
/// pressures on a chart of their own, and the binary
/// channels - vigilance alarm, motor current, braking - as bars in rows below,
/// with the clock and the kilometres along the axes.
/// </summary>
public sealed class ElectronicTapeView : TapeView
{
    private const double AxisRow = 22;
    private const double SpeedChart = 220;
    private const double ChartGap = 14;
    private const double PressureChart = 84;
    private const double PressureFullScaleBar = 6;
    private const double ChannelRow = 18;
    private const int Channels = 3;
    private const double LabelRepeat = 900;

    private const double SpeedTop = AxisRow;
    private const double SpeedBottom = SpeedTop + SpeedChart;
    private const double PressureTop = SpeedBottom + ChartGap;
    private const double PressureBottom = PressureTop + PressureChart;
    private const double ChannelsTop = PressureBottom + ChartGap;
    private const double ChannelsBottom = ChannelsTop + Channels * ChannelRow;

    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#F4F6F8"));
    private static readonly IBrush PlotBrush = Brushes.White;
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#3A4450"));
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.Parse("#7B8794"));
    private static readonly IPen FramePen = new Pen(new SolidColorBrush(Color.Parse("#B9C2CC")), 1);
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#E4E8ED")), 1);
    private static readonly IPen GridBoldPen = new Pen(new SolidColorBrush(Color.Parse("#CED5DD")), 1);
    private static readonly IPen SpeedPen = new Pen(new SolidColorBrush(Color.Parse("#1565C0")), 1.6,
                                                    lineJoin: PenLineJoin.Round);
    private static readonly IPen PipePen = new Pen(new SolidColorBrush(Color.Parse("#2E7D32")), 1.3,
                                                   lineJoin: PenLineJoin.Round);
    private static readonly IPen CylinderPen = new Pen(new SolidColorBrush(Color.Parse("#C62828")), 1.3,
                                                       lineJoin: PenLineJoin.Round);
    private static readonly IBrush ShpBrush = new SolidColorBrush(Color.Parse("#F9A825"));
    private static readonly IBrush CurrentBrush = new SolidColorBrush(Color.Parse("#5C6BC0"));
    private static readonly IBrush BrakeBrush = new SolidColorBrush(Color.Parse("#E53935"));
    private static readonly Typeface Face = new(FontFamily.Default);

    protected override double LeftMargin => 74;

    protected override double TapeHeight => ChannelsBottom + AxisRow + 4;

    private double SpeedY(double speed) => SpeedBottom - SpeedChart * Math.Min(speed, TopSpeed) / TopSpeed;

    private static double PressureY(double bar) =>
        PressureBottom - PressureChart * Math.Clamp(bar / PressureFullScaleBar, 0, 1);

    protected override void DrawBlank(DrawingContext context) =>
        context.DrawRectangle(Background, null, new Rect(0, 0, Bounds.Width, TapeHeight));

    protected override void DrawTape(DrawingContext context, HaslerTape tape)
    {
        double left = LeftMargin, right = TapeRight;
        foreach (var (top, bottom) in new[] { (SpeedTop, SpeedBottom), (PressureTop, PressureBottom),
                                              (ChannelsTop, ChannelsBottom) })
            context.DrawRectangle(PlotBrush, FramePen, new Rect(left, top, right - left, bottom - top));

        DrawValueGrid(context, left, right);
        DrawTimeAxis(context, tape);
        DrawDistanceAxis(context, tape);

        context.DrawGeometry(null, PipePen, Trace(tape, s => PressureY(s.PipePressBar)));
        context.DrawGeometry(null, CylinderPen, Trace(tape, s => PressureY(s.BrakePressBar)));
        context.DrawGeometry(null, SpeedPen, Trace(tape, s => SpeedY(s.SpeedKmh)));

        DrawChannel(context, tape, 0, s => s.SecurityAlarm, ShpBrush);
        DrawChannel(context, tape, 1, s => s.Current, CurrentBrush);
        DrawChannel(context, tape, 2, s => s.Braking, BrakeBrush);

        DrawLegend(context);
    }

    /// <summary>The value lines and their figures, repeated along a long trip so a scrolled view still reads.</summary>
    private void DrawValueGrid(DrawingContext context, double left, double right)
    {
        int step = TapeFigures.SpeedStep(SpeedChart / TopSpeed);
        for (int v = 0; v <= TopSpeed; v += step)
        {
            double y = SpeedY(v);
            context.DrawLine(v % (5 * step) == 0 ? GridBoldPen : GridPen, new Point(left, y), new Point(right, y));
            if (v % (2 * step) == 0)
                for (double x = left; x < right; x += LabelRepeat)
                    Print(context, v.ToString(CultureInfo.InvariantCulture), x == left ? 30 : x + 4, y - 7, 10,
                          x == left ? TextBrush : DimBrush);
        }

        for (int bar = 0; bar <= PressureFullScaleBar; bar++)
        {
            double y = PressureY(bar);
            context.DrawLine(bar == 5 ? GridBoldPen : GridPen, new Point(left, y), new Point(right, y));
            if (bar % 2 == 0)
                Print(context, bar.ToString(CultureInfo.InvariantCulture), 52, y - 7, 10, TextBrush);
        }

        for (int i = 1; i < Channels; i++)
            context.DrawLine(GridPen, new Point(left, ChannelsTop + i * ChannelRow),
                             new Point(right, ChannelsTop + i * ChannelRow));
    }

    /// <summary>The clock along the top edge: a line every minute of uniform time, or ticks where minutes pass on a distance axis.</summary>
    private void DrawTimeAxis(DrawingContext context, HaslerTape tape)
    {
        double pxPerMinute = ByDistance
            ? Scale / Math.Max(tape.Duration / 60.0 / Math.Max(Length, 0.001), 0.001)
            : Scale;
        double step = TapeFigures.MinuteStep(pxPerMinute);
        double next = Math.Ceiling(ClockAt(tape.Samples[0]) / 60 / step) * step * 60;
        double lastLabel = double.NegativeInfinity;
        foreach (var s in tape.Samples)
        {
            double clock = ClockAt(s);
            if (clock < next)
                continue;
            double x = XOf(s);
            context.DrawLine(GridPen, new Point(x, SpeedTop), new Point(x, SpeedBottom));
            context.DrawLine(FramePen, new Point(x, SpeedTop - 4), new Point(x, SpeedTop));
            if (x - lastLabel > 46)
            {
                var t = TimeSpan.FromSeconds(next % 86400);
                Print(context, $"{(int)t.TotalHours:00}:{t.Minutes:00}", x - 14, 3, 10, TextBrush);
                lastLabel = x;
            }
            next = (Math.Floor(clock / 60 / step) + 1) * step * 60;
        }
        Print(context, "h:min", 4, 3, 10, DimBrush);
    }

    /// <summary>Kilometres along the bottom edge.</summary>
    private void DrawDistanceAxis(DrawingContext context, HaslerTape tape)
    {
        double stepKm = TapeFigures.KmStep(ByDistance ? Scale : Scale * tape.Duration / 60 / Math.Max(tape.DistanceM / 1000, 0.001));
        double next = 0;
        double y = ChannelsBottom;
        double lastLabel = double.NegativeInfinity;
        foreach (var s in tape.Samples)
        {
            if (s.DistanceM < next * 1000)
                continue;
            double x = XOf(s);
            context.DrawLine(FramePen, new Point(x, y), new Point(x, y + 4));
            if (ByDistance)
                context.DrawLine(GridPen, new Point(x, PressureTop), new Point(x, PressureBottom));
            if (x - lastLabel > 40)
            {
                Print(context, next.ToString("0.#", CultureInfo.InvariantCulture), x - 6, y + 5, 10, TextBrush);
                lastLabel = x;
            }
            next = (Math.Floor(s.DistanceM / 1000 / stepKm) + 1) * stepKm;
        }
        Print(context, "km", 4, y + 5, 10, DimBrush);
    }

    private void DrawChannel(DrawingContext context, HaslerTape tape, int row, Func<TapeSample, bool> on, IBrush brush)
    {
        double top = ChannelsTop + row * ChannelRow + 4, height = ChannelRow - 8;
        double? start = null, last = LeftMargin;
        foreach (var s in tape.Samples)
        {
            double x = XOf(s);
            if (on(s))
                start ??= x;
            else if (start is { } from)
            {
                context.DrawRectangle(brush, null, new Rect(from, top, Math.Max(1, x - from), height));
                start = null;
            }
            last = x;
        }
        if (start is { } open)
            context.DrawRectangle(brush, null, new Rect(open, top, Math.Max(1, last.Value - open), height));
    }

    private void DrawLegend(DrawingContext context)
    {
        Print(context, "km/h", 4, SpeedTop + 2, 10, DimBrush);
        Print(context, "bar", 4, PressureTop + 2, 10, DimBrush);
        Print(context, App.Loc["HaslerTrackPipe"], 4, PressureTop + 22, 10, PipePen.Brush!);
        Print(context, App.Loc["HaslerTrackCylinders"], 4, PressureTop + 38, 10, CylinderPen.Brush!);
        Print(context, App.Loc["HaslerTrackShp"], 4, ChannelsTop + 2, 10, TextBrush);
        Print(context, App.Loc["HaslerTrackCurrent"], 4, ChannelsTop + ChannelRow + 2, 10, TextBrush);
        Print(context, App.Loc["HaslerTrackBrake"], 4, ChannelsTop + 2 * ChannelRow + 2, 10, TextBrush);
    }

    private static void Print(DrawingContext context, string text, double x, double y, double size, IBrush brush) =>
        context.DrawText(new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size,
                                           brush), new Point(x, y));
}
