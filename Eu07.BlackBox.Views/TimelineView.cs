using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Eu07.BlackBox.Views;

/// <summary>
/// The user's train over the time of the recording, by the clock of the simulation: its
/// speed, the speed the AI would hold and the next limit it saw, below them bands of who
/// drove and how against the AI, of the controller, the brakes, the alerter (CA), the cab
/// signalling (SHP) and the camera, and marks of the commands, the signals passed, the
/// stations, the horn points and the couplings. A click goes to its moment; the wheel zooms
/// the time, dragging moves it.
/// </summary>
public sealed class TimelineView : Control
{
    private const double Left = 56;
    private const double Right = 12;
    private const double Top = 22;
    private const double BandHeight = 8;
    private const double BandGap = 3;
    private const double MarksHeight = 16;
    private const double AxisHeight = 20;
    private const int Bands = 6;

    private Recording? _recording;
    private double _time;
    private double _start;
    private double _secondsPerPixel = -1;
    private Point? _pressed;
    private double _pressedStart;
    private bool _dragged;
    private double? _hover;
    private int _measured = -1;
    private Recording? _measuredOf;
    private double _speedTop = 40;
    private int _controllerTop = 1;

    /// <summary>A moment was clicked.</summary>
    public event Action<double>? Seek;

    /// <summary>The moment under the pointer, null once it leaves.</summary>
    public event Action<double?>? Hovered;

    public TimelineView()
    {
        ClipToBounds = true;
        MinHeight = 160;
    }

    public IBlackBoxStrings Strings { get; set; } = new DefaultBlackBoxStrings();

    public Recording? Recording
    {
        get => _recording;
        set
        {
            _recording = value;
            _secondsPerPixel = -1;
            _start = 0;
            InvalidateVisual();
        }
    }

    public double Time
    {
        get => _time;
        set
        {
            _time = value;
            // Kept in view while it plays.
            if (_secondsPerPixel > 0 && _recording is not null)
            {
                double end = _start + PlotWidth * _secondsPerPixel;
                if (value > end || value < _start)
                    _start = Math.Max(0, value - PlotWidth * _secondsPerPixel * 0.1);
            }
            InvalidateVisual();
        }
    }

    /// <summary>All of the recording in view.</summary>
    public void Fit()
    {
        _secondsPerPixel = -1;
        _start = 0;
        InvalidateVisual();
    }

    private double PlotWidth => Math.Max(10, Bounds.Width - Left - Right);
    private double ChartBottom => Bounds.Height - AxisHeight - MarksHeight - Bands * (BandHeight + BandGap) - 6;
    private double X(double t) => Left + (t - _start) / _secondsPerPixel;
    private double TimeAt(double x) => _start + (x - Left) * _secondsPerPixel;

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        var recording = _recording;
        if (recording is null || recording.Duration <= 0)
            return;
        if (_secondsPerPixel <= 0)
            _secondsPerPixel = Math.Max(recording.Duration, 60) / PlotWidth;

        double end = _start + PlotWidth * _secondsPerPixel;
        double chartBottom = ChartBottom;
        Measure(recording);
        double top = _speedTop;
        double Y(double kmh) => chartBottom - Math.Clamp(kmh / top, 0, 1.05) * (chartBottom - Top);

        using (context.PushClip(new Rect(Left, 0, PlotWidth, size.Height)))
        {
            DrawGrid(context, top, Y, size);
            DrawCurves(context, recording, Y, end);
            double bands = chartBottom + 6;
            DrawBands(context, recording, bands, end);
            DrawMarks(context, recording, bands + Bands * (BandHeight + BandGap), end);
            DrawAxis(context, recording, size.Height - AxisHeight, end);
            if (_hover is { } hover)
                context.DrawLine(new Pen(Palette.DimText, 1, new DashStyle(new double[] { 2, 3 }, 0)),
                                 new Point(X(hover), Top), new Point(X(hover), size.Height - AxisHeight));
            double cursor = X(_time);
            context.DrawLine(new Pen(Palette.Cursor, 1.5), new Point(cursor, Top - 6), new Point(cursor, size.Height - AxisHeight));
            // The moment played, on the axis.
            var clock = MapView.Text(recording.ClockAt(_time).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture), Palette.Cursor, 10, FontWeight.SemiBold);
            var bubble = new Rect(Math.Clamp(cursor - clock.Width / 2 - 5, Left, size.Width - Right - clock.Width - 10), size.Height - AxisHeight + 2,
                                  clock.Width + 10, clock.Height + 2);
            context.DrawRectangle(Palette.Card, new Pen(Palette.Cursor, 1), bubble);
            context.DrawText(clock, new Point(bubble.X + 5, bubble.Y + 1));
        }
        DrawSpeedLabels(context, top, Y);
        DrawBandLabels(context, chartBottom + 6);
        DrawKey(context);
    }

    /// <summary>The top of the speed scale and the highest step of the controller, once a reading.</summary>
    private void Measure(Recording recording)
    {
        if (_measured == recording.Revision && ReferenceEquals(_measuredOf, recording))
            return;
        float max = 40;
        int controller = 1;
        foreach (var s in recording.Cab)
        {
            max = Math.Max(max, s.AbsSpeedKmh);
            if (s.AiValid)
                max = Math.Max(max, s.Ai.TargetKmh);
            controller = Math.Max(controller, (int)s.MainController);
        }
        _speedTop = Math.Ceiling(max * 1.1 / 20) * 20;
        _controllerTop = controller;
        _measured = recording.Revision;
        _measuredOf = recording;
    }

    private void DrawGrid(DrawingContext context, double top, Func<double, double> y, Size size)
    {
        var pen = new Pen(Palette.Grid, 1);
        for (double v = 0; v <= top; v += top > 160 ? 40 : 20)
            context.DrawLine(pen, new Point(Left, y(v)), new Point(size.Width - Right, y(v)));
    }

    private void DrawSpeedLabels(DrawingContext context, double top, Func<double, double> y)
    {
        for (double v = 0; v <= top; v += top > 160 ? 40 : 20)
        {
            var text = MapView.Text(v.ToString("0", CultureInfo.InvariantCulture), Palette.DimText, 10);
            context.DrawText(text, new Point(Left - 6 - text.Width, y(v) - text.Height / 2));
        }
    }

    /// <summary>
    /// Each curve by columns of pixels: in a column the lowest and the highest speed,
    /// so an hour draws as fast as a minute.
    /// </summary>
    private void DrawCurves(DrawingContext context, Recording recording, Func<double, double> y, double end)
    {
        var cab = recording.Cab;
        int first = Math.Max(0, recording.CabIndexAt(_start));
        var speed = new Curve();
        var target = new Curve();
        var limits = new List<Point>();
        int column = int.MinValue;
        CabSample? before = null;
        for (int i = first; i < cab.Count; i++)
        {
            var s = cab[i];
            if (s.Time > end + _secondsPerPixel * 2)
                break;
            bool gap = before is { } b && (s.Time - b.Time > 1.0 || s.Vehicle != b.Vehicle);
            if (gap)
            {
                speed.Break();
                target.Break();
            }
            int c = (int)Math.Round(X(s.Time));
            speed.Add(c, y(s.AbsSpeedKmh));
            // Under the autopilot too: what it aimed at.
            if (s.AiValid)
                target.Add(c, y(s.Ai.TargetKmh));
            else
                target.Break();
            if (c != column && s.AiValid && s.Ai.HasNextLimit)
                limits.Add(new Point(c, y(s.Ai.NextLimitKmh)));
            column = c;
            before = s;
        }
        if (speed.Fill(y(0)) is { } fill)
            context.DrawGeometry(Palette.SpeedFill, null, fill);
        if (target.Geometry() is { } t)
            context.DrawGeometry(null, new Pen(Palette.Target, 1.5), t);
        var limitPen = new Pen(Palette.Limit, 2);
        foreach (var p in limits)
            context.DrawLine(limitPen, p, p + new Vector(1, 0));
        if (speed.Geometry() is { } v)
            context.DrawGeometry(null, new Pen(Palette.Speed, 1.5), v);
    }

    private void DrawBands(DrawingContext context, Recording recording, double top, double end)
    {
        var cab = recording.Cab;
        int first = Math.Max(0, recording.CabIndexAt(_start));
        var cameras = recording.Cameras;
        int camera = -1;
        var rows = new BandRun[Bands];
        for (int row = 0; row < rows.Length; row++)
            rows[row] = new BandRun(context, top + row * (BandHeight + BandGap));
        for (int i = first; i < cab.Count; i++)
        {
            var s = cab[i];
            if (s.Time > end)
                break;
            double next = i + 1 < cab.Count && cab[i + 1].Time - s.Time <= 1.0 ? cab[i + 1].Time : s.Time + 0.1;
            double x0 = X(s.Time), x1 = Math.Max(X(next), x0 + 0.6);
            rows[0].Add(x0, x1, Palette.Of(DrivingSummary.Judge(s)), BandHeight);
            // The controller as high as its step stands against the highest one used.
            rows[1].Add(x0, x1, s.MainController > 0 ? Palette.Controller : null,
                        Math.Max(1.5, BandHeight * s.MainController / _controllerTop));
            rows[2].Add(x0, x1, s.BrakeCylinder > 0.2f ? BrakeShade(s.BrakeCylinder) : null, BandHeight);
            if (s.Security != SecurityFlags.None || s.CabEnd != 0)
            {
                // The alerter and the cab signalling apart, red when not acknowledged in time.
                rows[3].Add(x0, x1, s.Has(SecurityFlags.VigilanceBeeping) ? Palette.LampAlarm
                                    : s.Has(SecurityFlags.Vigilance) ? Palette.Vigilance : null, BandHeight);
                rows[4].Add(x0, x1, s.Has(SecurityFlags.CabSignalBeeping) ? Palette.LampAlarm
                                    : s.Has(SecurityFlags.CabSignal) ? Palette.CabSignal : null, BandHeight);
                if (s.Has(SecurityFlags.Braking))
                    rows[s.Has(SecurityFlags.CabSignal) && !s.Has(SecurityFlags.Vigilance) ? 4 : 3].Add(x0, x1, Palette.Bad, BandHeight);
            }
            else
                rows[3].Add(x0, x1, (s.Flags & CabFlags.AlerterBeeping) != 0 ? Palette.LampAlarm
                                    : (s.Flags & CabFlags.AlerterBlinking) != 0 ? Palette.Vigilance : null, BandHeight);
            // Where the user looked from: the samples go on in time, and so does the camera.
            while (camera + 1 < cameras.Count && cameras[camera + 1].Time <= s.Time)
                camera++;
            rows[5].Add(x0, x1, camera < 0 ? null : CameraBrush(cameras[camera].Mode), BandHeight);
        }
        foreach (var row in rows)
            row.Flush();
    }

    private static readonly IBrush CameraCab = Palette.Faded(Palette.UserTrain, 0.45);
    private static readonly IBrush CameraOutside = Palette.Faded(Palette.HornPoint, 0.75);
    private static readonly IBrush CameraFree = Palette.Faded(Palette.Station, 0.75);

    private static IBrush CameraBrush(CameraMode mode) => mode switch
    {
        CameraMode.Cab => CameraCab,
        CameraMode.External => CameraOutside,
        _ => CameraFree
    };

    private static readonly IBrush[] BrakeShades = Shades(Palette.Brakes, 8);

    private static IBrush[] Shades(IBrush brush, int count)
    {
        var colour = ((ISolidColorBrush)brush).Color;
        var shades = new IBrush[count];
        for (int i = 0; i < count; i++)
            shades[i] = new SolidColorBrush(colour, 0.35 + 0.65 * i / (count - 1));
        return shades;
    }

    /// <summary>The harder the brakes hold, the deeper the red.</summary>
    private static IBrush BrakeShade(float cylinderBar) =>
        BrakeShades[(int)Math.Round(Math.Clamp(cylinderBar / 5.0, 0, 1) * (BrakeShades.Length - 1))];

    /// <summary>
    /// The samples of a band drawn as few rectangles as the band changes: ten a second, each
    /// narrower than a pixel, they would leave seams between them.
    /// </summary>
    private sealed class BandRun(DrawingContext context, double top)
    {
        private IBrush? _brush;
        private double _height, _from, _to;

        public void Add(double x0, double x1, IBrush? brush, double height)
        {
            if (brush is not null && ReferenceEquals(brush, _brush) && height == _height && x0 <= _to + 0.5)
            {
                _to = Math.Max(_to, x1);
                return;
            }
            Flush();
            _brush = brush;
            _height = height;
            _from = x0;
            _to = x1;
        }

        public void Flush()
        {
            if (_brush is not null)
                context.FillRectangle(_brush, new Rect(_from, top + BandHeight - _height, _to - _from, _height));
            _brush = null;
        }
    }

    private void DrawBandLabels(DrawingContext context, double top)
    {
        string[] names = { Strings["BandDriving"], Strings["BandController"], Strings["BandBrakes"], Strings["BandVigilance"],
                           Strings["BandCabSignal"], Strings["BandCamera"] };
        for (int i = 0; i < names.Length; i++)
        {
            double y = top + i * (BandHeight + BandGap);
            context.DrawRectangle(Palette.Faded(Palette.Grid, 0.5), null, new Rect(Left, y, Bounds.Width - Left - Right, BandHeight));
            var text = MapView.Text(names[i], Palette.FaintText, 9);
            context.DrawText(text, new Point(Left - 6 - text.Width, y + (BandHeight - text.Height) / 2));
        }
    }

    /// <summary>
    /// The commands as short grey ticks, the autopilot switched in blue, the signals passed in
    /// green, at danger tall and red; the stations as blue plates, the horn points as diamonds
    /// green or red as the horn sounded, the couplings as yellow ticks.
    /// </summary>
    private void DrawMarks(DrawingContext context, Recording recording, double top, double end)
    {
        foreach (var station in recording.UserStations())
        {
            if (station.Time < _start || station.Time > end || station.Kind == StationKind.Ready)
                continue;
            double x = X(station.Time);
            var brush = station.Kind == StationKind.Missed ? Palette.Bad : Palette.Station;
            context.DrawLine(new Pen(Palette.Faded(brush, 0.35), 1), new Point(x, Top), new Point(x, top));
            context.DrawRectangle(brush, null, new Rect(x - 3, Top - 13, 6, 8), 1.5, 1.5);
        }
        foreach (var horn in recording.UserHornChecks())
        {
            if (horn.Request.Time < _start || horn.Request.Time > end)
                continue;
            double x = X(horn.Request.Time);
            var brush = horn.Sounded ? Palette.Good : Palette.Bad;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(x, Top - 14), true);
                g.LineTo(new Point(x + 4, Top - 10));
                g.LineTo(new Point(x, Top - 6));
                g.LineTo(new Point(x - 4, Top - 10));
                g.EndFigure(true);
            }
            context.DrawGeometry(brush, null, geometry);
        }
        var coupling = new Pen(Palette.Warn, 2);
        foreach (var c in recording.UserCouplings())
            if (c.Time >= _start && c.Time <= end)
                context.DrawLine(coupling, new Point(X(c.Time), top + 1), new Point(X(c.Time), top + MarksHeight));
        var command = new Pen(Palette.Command, 1);
        foreach (var c in recording.UserCommands())
            if (c.Time >= _start && c.Time <= end)
                context.DrawLine(command, new Point(X(c.Time), top + MarksHeight - 5), new Point(X(c.Time), top + MarksHeight));
        var autopilot = new Pen(Palette.Autopilot, 2);
        foreach (var d in recording.DriverChanges)
            if (d.Time >= _start && d.Time <= end)
                context.DrawLine(autopilot, new Point(X(d.Time), top + 2), new Point(X(d.Time), top + MarksHeight));
        foreach (var p in recording.UserPassings())
        {
            if (p.Time < _start || p.Time > end)
                continue;
            double x = X(p.Time);
            if (p.AtDanger)
            {
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(new Point(x, Top - 4), true);
                    g.LineTo(new Point(x - 5, Top - 13));
                    g.LineTo(new Point(x + 5, Top - 13));
                    g.EndFigure(true);
                }
                context.DrawGeometry(Palette.Over, null, geometry);
                context.DrawLine(new Pen(Palette.Over, 1.5), new Point(x, Top - 4), new Point(x, top + MarksHeight));
            }
            else
                context.DrawLine(new Pen(Palette.Within, 1.5), new Point(x, top + 1), new Point(x, top + MarksHeight - 6));
        }
    }

    private void DrawAxis(DrawingContext context, Recording recording, double y, double end)
    {
        double[] steps = { 10, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200 };
        double step = steps[^1];
        foreach (double candidate in steps)
            if (candidate / _secondsPerPixel >= 80)
            {
                step = candidate;
                break;
            }
        var pen = new Pen(Palette.DimText, 1);
        // On the clock of the simulation: the marks fall on whole minutes and hours of it.
        double offset = recording.ClockAt(0).TotalSeconds;
        double first = Math.Ceiling((_start + offset) / step) * step - offset;
        for (double t = first; t <= end; t += step)
        {
            double x = X(t);
            context.DrawLine(pen, new Point(x, y), new Point(x, y + 4));
            var clock = recording.ClockAt(t);
            string label = step < 60 ? clock.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : clock.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            var text = MapView.Text(label, Palette.DimText, 10);
            context.DrawText(text, new Point(x - text.Width / 2, y + 4));
        }
    }

    private void DrawKey(DrawingContext context)
    {
        double x = Left + 4;
        foreach (var (brush, key) in new[] { (Palette.Speed, "TimelineSpeed"), (Palette.Target, "TimelineTarget"), (Palette.Limit, "TimelineLimit") })
        {
            context.DrawLine(new Pen(brush, 2), new Point(x, 10), new Point(x + 14, 10));
            var text = MapView.Text(Strings[key], Palette.DimText, 10);
            context.DrawText(text, new Point(x + 18, 10 - text.Height / 2));
            x += 18 + text.Width + 14;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (e.ClickCount == 2)
        {
            Fit();
            return;
        }
        _pressed = e.GetPosition(this);
        _pressedStart = _start;
        _dragged = false;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_recording is null || _secondsPerPixel <= 0)
            return;
        var position = e.GetPosition(this);
        if (_pressed is { } from)
        {
            double moved = position.X - from.X;
            if (Math.Abs(moved) > 3)
                _dragged = true;
            if (_dragged)
                _start = Math.Clamp(_pressedStart - moved * _secondsPerPixel, 0, Math.Max(0, _recording.Duration - PlotWidth * _secondsPerPixel * 0.5));
        }
        _hover = position.X >= Left ? Math.Clamp(TimeAt(position.X), 0, _recording.Duration) : null;
        Hovered?.Invoke(_hover);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressed is not null && !_dragged && _recording is not null && _secondsPerPixel > 0)
            Seek?.Invoke(Math.Clamp(TimeAt(e.GetPosition(this).X), 0, _recording.Duration));
        _pressed = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        Hovered?.Invoke(null);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_recording is null || _secondsPerPixel <= 0)
            return;
        double x = e.GetPosition(this).X;
        double at = TimeAt(x);
        double whole = Math.Max(_recording.Duration, 60) / PlotWidth;
        _secondsPerPixel = Math.Clamp(_secondsPerPixel / Math.Pow(1.3, e.Delta.Y), 0.02, whole);
        _start = Math.Max(0, at - (x - Left) * _secondsPerPixel);
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>A line through columns of pixels: in each the first, the lowest, the highest and the last value.</summary>
    private sealed class Curve
    {
        private readonly List<List<Point>> _lines = new();
        private List<Point>? _line;
        private int _column = int.MinValue;
        private double _first, _low, _high, _last;

        public void Add(int column, double y)
        {
            if (column != _column)
            {
                Flush();
                _column = column;
                _first = _low = _high = y;
            }
            _low = Math.Min(_low, y);
            _high = Math.Max(_high, y);
            _last = y;
        }

        public void Break()
        {
            Flush();
            _line = null;
            _column = int.MinValue;
        }

        private void Flush()
        {
            if (_column == int.MinValue)
                return;
            if (_line is null)
                _lines.Add(_line = new List<Point>());
            var (a, b) = _first <= _last ? (_low, _high) : (_high, _low);
            _line.Add(new Point(_column, _first));
            if (Math.Abs(_high - _low) > 0.5)
            {
                _line.Add(new Point(_column, a));
                _line.Add(new Point(_column, b));
            }
            _line.Add(new Point(_column, _last));
        }

        /// <summary>The area under the line down to the bottom given, for a soft fill.</summary>
        public StreamGeometry? Fill(double bottom)
        {
            Flush();
            _column = int.MinValue;
            var geometry = new StreamGeometry();
            bool any = false;
            using (var g = geometry.Open())
            {
                foreach (var line in _lines)
                {
                    if (line.Count < 2)
                        continue;
                    g.BeginFigure(new Point(line[0].X, bottom), true);
                    foreach (var point in line)
                        g.LineTo(point);
                    g.LineTo(new Point(line[^1].X, bottom));
                    g.EndFigure(true);
                    any = true;
                }
            }
            return any ? geometry : null;
        }

        public StreamGeometry? Geometry()
        {
            Flush();
            _column = int.MinValue;
            var geometry = new StreamGeometry();
            bool any = false;
            using (var g = geometry.Open())
            {
                foreach (var line in _lines)
                {
                    if (line.Count < 2)
                        continue;
                    g.BeginFigure(line[0], false);
                    for (int i = 1; i < line.Count; i++)
                        g.LineTo(line[i]);
                    g.EndFigure(false);
                    any = true;
                }
            }
            return any ? geometry : null;
        }
    }
}
