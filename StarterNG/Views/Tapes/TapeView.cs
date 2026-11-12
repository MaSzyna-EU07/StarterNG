using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using StarterNG.Domain.Tapes;

namespace StarterNG.Views.Tapes;

/// <summary>
/// What every way of drawing a recorded trip shares: the trip, how it is laid
/// along the x axis - by distance or by time, at a scale - the clock it started
/// at, and the pointer read-out.
/// </summary>
public abstract class TapeView : Control
{
    public static readonly StyledProperty<HaslerTape?> TapeProperty =
        AvaloniaProperty.Register<TapeView, HaslerTape?>(nameof(Tape));

    public static readonly StyledProperty<bool> ByDistanceProperty =
        AvaloniaProperty.Register<TapeView, bool>(nameof(ByDistance), true);

    /// <summary>Pixels per kilometre when fed by distance, per minute when fed by time.</summary>
    public static readonly StyledProperty<double> ScaleProperty =
        AvaloniaProperty.Register<TapeView, double>(nameof(Scale), 40);

    /// <summary>
    /// The time of day the recording started at. The log only counts seconds, so
    /// without it the clock counts from the start of the trip.
    /// </summary>
    public static readonly StyledProperty<TimeSpan> StartClockProperty =
        AvaloniaProperty.Register<TapeView, TimeSpan>(nameof(StartClock));

    /// <summary>
    /// The moment a black box of the same run is at, in the tape's own seconds: both count the simulation from
    /// its start, paused or not. Null draws nothing.
    /// </summary>
    public static readonly StyledProperty<double?> MarkProperty =
        AvaloniaProperty.Register<TapeView, double?>(nameof(Mark));

    private static readonly IPen CursorPen = new Pen(new SolidColorBrush(Color.Parse("#FFB000")), 1);
    private static readonly IBrush MarkBrush = new SolidColorBrush(Color.Parse("#4FC3F7"));
    private static readonly IPen MarkPen = new Pen(MarkBrush, 2);

    static TapeView()
    {
        AffectsMeasure<TapeView>(TapeProperty, ByDistanceProperty, ScaleProperty);
        AffectsRender<TapeView>(TapeProperty, ByDistanceProperty, ScaleProperty, StartClockProperty, MarkProperty);
    }

    public HaslerTape? Tape
    {
        get => GetValue(TapeProperty);
        set => SetValue(TapeProperty, value);
    }

    public bool ByDistance
    {
        get => GetValue(ByDistanceProperty);
        set => SetValue(ByDistanceProperty, value);
    }

    public double Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public TimeSpan StartClock
    {
        get => GetValue(StartClockProperty);
        set => SetValue(StartClockProperty, value);
    }

    public double? Mark
    {
        get => GetValue(MarkProperty);
        set => SetValue(MarkProperty, value);
    }

    /// <summary>The sample under the pointer, or null once it leaves the tape.</summary>
    public event Action<TapeSample?>? Hovered;

    /// <summary>The sample clicked on.</summary>
    public event Action<TapeSample>? Picked;

    protected abstract double LeftMargin { get; }

    private const double RightMargin = 24;

    protected abstract double TapeHeight { get; }

    protected double? CursorX { get; private set; }

    protected double TopSpeed => TapeFigures.TopSpeed(Tape?.MaxSpeedKmh ?? 0);

    protected double Length => Tape is null ? 0 : ByDistance ? Tape.DistanceM / 1000.0 : Tape.Duration / 60.0;

    protected double TapeRight => LeftMargin + Length * Scale;

    protected double XOf(TapeSample s) =>
        LeftMargin + (ByDistance ? s.DistanceM / 1000.0 : (s.Time - (Tape?.StartTime ?? 0)) / 60.0) * Scale;

    private double PositionOf(double x) =>
        ByDistance ? (x - LeftMargin) / Scale * 1000.0 : (x - LeftMargin) / Scale * 60.0 + (Tape?.StartTime ?? 0);

    /// <summary>
    /// Where the mark stands across the tape; null with no mark or outside the trip. By distance it is where the
    /// train was at that moment, so through a stop the mark stands still.
    /// </summary>
    public double? MarkX => Mark is { } t ? XAt(t) : null;

    /// <summary>Where a moment of the trip stands across the tape; null outside the trip.</summary>
    public double? XAt(double t)
    {
        var tape = Tape;
        if (tape is null || tape.IsEmpty || t < tape.StartTime || t > tape.Samples[^1].Time)
            return null;
        return ByDistance
            ? XOf(tape.Samples[tape.IndexAt(t, byDistance: false)])
            : LeftMargin + (t - tape.StartTime) / 60.0 * Scale;
    }

    /// <summary>Seconds since midnight at the sample, by the clock the trip started at.</summary>
    public double ClockAt(TapeSample s) => StartClock.TotalSeconds + s.Time - (Tape?.StartTime ?? 0);

    /// <summary>What the view shows, for another one to show the same.</summary>
    public void CopySettingsFrom(TapeView other)
    {
        Tape = other.Tape;
        ByDistance = other.ByDistance;
        Scale = other.Scale;
        StartClock = other.StartClock;
        Mark = other.Mark;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(TapeRight + RightMargin, TapeHeight);

    public sealed override void Render(DrawingContext context)
    {
        DrawBlank(context);

        var tape = Tape;
        if (tape is null || tape.IsEmpty)
            return;

        DrawTape(context, tape);
        DrawMark(context);
        DrawCursor(context);
    }

    /// <summary>What is there with no trip on it.</summary>
    protected abstract void DrawBlank(DrawingContext context);

    /// <summary>A trip with something in it, over the blank.</summary>
    protected abstract void DrawTape(DrawingContext context, HaslerTape tape);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var tape = Tape;
        if (tape is null || tape.IsEmpty)
            return;

        double x = Math.Clamp(e.GetPosition(this).X, LeftMargin, TapeRight);
        int index = tape.IndexAt(PositionOf(x), ByDistance);
        CursorX = x;
        InvalidateVisual();
        Hovered?.Invoke(index < 0 ? null : tape.Samples[index]);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var tape = Tape;
        if (tape is null || tape.IsEmpty || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        double x = Math.Clamp(e.GetPosition(this).X, LeftMargin, TapeRight);
        int index = tape.IndexAt(PositionOf(x), ByDistance);
        if (index >= 0)
            Picked?.Invoke(tape.Samples[index]);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        CursorX = null;
        InvalidateVisual();
        Hovered?.Invoke(null);
    }

    /// <summary>A line across the tape with a notch on top, in a colour of its own: not the pointer's.</summary>
    private void DrawMark(DrawingContext context)
    {
        if (MarkX is not { } x)
            return;
        context.DrawLine(MarkPen, new Point(x, 0), new Point(x, TapeHeight));
        var notch = new StreamGeometry();
        using (var g = notch.Open())
        {
            g.BeginFigure(new Point(x - 5, 0), true);
            g.LineTo(new Point(x + 5, 0));
            g.LineTo(new Point(x, 7));
            g.EndFigure(true);
        }
        context.DrawGeometry(MarkBrush, null, notch);
    }

    private void DrawCursor(DrawingContext context)
    {
        if (CursorX is { } x)
            context.DrawLine(CursorPen, new Point(x, 0), new Point(x, TapeHeight));
    }

    /// <summary>
    /// A record as one line, one column of pixels at a time: per column the
    /// lowest and highest point in it, so a long trip draws as fast as a short
    /// one and what happens during a stop on a distance-fed tape still shows as
    /// one stroke.
    /// </summary>
    protected StreamGeometry Trace(HaslerTape tape, Func<TapeSample, double> y)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        bool begun = false;
        int column = int.MinValue;
        double top = 0, bottom = 0, first = 0, last = 0;

        void Flush()
        {
            if (column == int.MinValue)
                return;
            // Enter where the column started, leave where it ended.
            var (a, b) = first <= last ? (top, bottom) : (bottom, top);
            foreach (double v in new[] { first, a, b, last })
            {
                var p = new Point(column, v);
                if (!begun)
                {
                    g.BeginFigure(p, false);
                    begun = true;
                }
                else
                    g.LineTo(p);
            }
        }

        foreach (var s in tape.Samples)
        {
            int c = (int)Math.Round(XOf(s));
            double value = y(s);
            if (c != column)
            {
                Flush();
                column = c;
                top = bottom = first = value;
            }
            top = Math.Min(top, value);
            bottom = Math.Max(bottom, value);
            last = value;
        }
        Flush();
        if (begun)
            g.EndFigure(false);
        return geometry;
    }
}
