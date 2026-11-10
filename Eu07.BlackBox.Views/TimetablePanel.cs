using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Eu07.BlackBox.Views;

/// <summary>
/// The timetable of the user's train, a row a station: what the timetable said and what
/// happened, by the end of the recording read so far. A stop shows the time of arrival and
/// of departure with the delay, how well the train was stopped against the stop point and
/// whether it stood along the platform; a station passed through the time of passing. The
/// station of the moment played is marked; a click goes to the moment the train was there.
/// </summary>
public sealed class TimetablePanel : Control
{
    private const double Pad = 12;
    private const double HeaderHeight = 50;
    private const double RowHeight = 54;

    private Recording? _recording;
    private double _time;
    private int _builtRevision = -1;
    private IReadOnlyList<StationProgress> _rows = Array.Empty<StationProgress>();
    private Timetable? _timetable;
    private int _hover = -1;

    public event Action<double>? Seek;

    public IBlackBoxStrings Strings { get; set; } = new DefaultBlackBoxStrings();

    public TimetablePanel()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public Recording? Recording
    {
        get => _recording;
        set
        {
            _recording = value;
            _builtRevision = -1;
            Rebuild();
        }
    }

    public double Time
    {
        get => _time;
        set
        {
            _time = value;
            Rebuild();
            InvalidateVisual();
        }
    }

    private void Rebuild()
    {
        if (_recording is not { } recording)
        {
            _rows = Array.Empty<StationProgress>();
            _timetable = null;
            InvalidateMeasure();
            return;
        }
        var timetable = recording.TimetableAt(_time);
        if (recording.Revision == _builtRevision && ReferenceEquals(timetable, _timetable))
            return;
        _builtRevision = recording.Revision;
        _timetable = timetable;
        _rows = StationProgress.Of(recording, _time);
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(0, _timetable is null ? 80 : HeaderHeight + _rows.Count * RowHeight + Pad);

    /// <summary>The row of the station the train was at, or made for, at the moment played.</summary>
    private int Current()
    {
        int current = -1;
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            double? at = row.ArrivedAt ?? row.DepartedAt;
            if (at is { } t && t <= _time + 0.05)
                current = i;
        }
        // Still standing there, or on the way to the next one.
        if (current >= 0 && _rows[current].DepartedAt is { } left && left <= _time && current + 1 < _rows.Count)
            return current + 1;
        return current < 0 ? (_rows.Count > 0 ? 0 : -1) : current;
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        var culture = CultureInfo.CurrentCulture;
        if (_timetable is not { } timetable)
        {
            context.DrawText(MapView.Text(Strings["TimetableNone"], Palette.DimText, 12), new Point(Pad, Pad));
            return;
        }

        // The train: its number and category, from where to where.
        var train = MapView.Text($"{timetable.Category} {timetable.Train}".Trim(), Palette.Text, 15, FontWeight.SemiBold);
        context.DrawText(train, new Point(Pad, 6));
        string relation = string.Format(culture, Strings["Relation"], timetable.From, timetable.To);
        if (timetable.Vmax > 0)
            relation += $"  ·  {timetable.Vmax:0} km/h";
        context.DrawText(MapView.Text(relation, Palette.DimText, 11), new Point(Pad, 6 + train.Height + 1));

        int current = Current();
        double rail = Pad + 6;
        for (int i = 0; i < _rows.Count; i++)
        {
            double top = HeaderHeight + i * RowHeight;
            var row = _rows[i];
            if (i == current)
            {
                context.DrawRectangle(Palette.CardHover, null, new Rect(0, top, size.Width, RowHeight - 1));
                context.DrawRectangle(Palette.Accent, null, new Rect(0, top, 2, RowHeight - 1));
            }
            else if (i == _hover)
                context.DrawRectangle(Palette.Faded(Palette.CardHover, 0.6), null, new Rect(0, top, size.Width, RowHeight - 1));

            // The line of the route through the stations, a dot each: hollow ahead, filled when done.
            var outcome = row.Outcome;
            var colour = outcome switch
            {
                StationOutcome.Missed => Palette.Bad,
                StationOutcome.Ahead => Palette.FaintText,
                _ => DelayBrush(row.ArrivalDelay ?? row.DepartureDelay)
            };
            double dotY = top + 15;
            if (i + 1 < _rows.Count)
                context.DrawLine(new Pen(Palette.Grid, 2), new Point(rail, dotY), new Point(rail, dotY + RowHeight));
            bool stop = row.Row.IsStop;
            double radius = stop ? 5.5 : 3.5;
            context.DrawEllipse(outcome == StationOutcome.Ahead ? Palette.Card : colour, new Pen(colour, 2), new Point(rail, dotY), radius, radius);

            // The station and what the timetable said.
            double x = rail + 16;
            var name = MapView.Text(StationNames.Display(row.Row.Station), outcome == StationOutcome.Ahead ? Palette.DimText : Palette.Text, 13,
                                    stop ? FontWeight.SemiBold : FontWeight.Normal);
            context.DrawText(name, new Point(x, dotY - name.Height / 2));
            string planned = stop
                ? $"{Minutes(row.Row.Arrival)}  →  {Minutes(row.Row.Departure)}"
                : Minutes(row.Row.Departure);
            var plan = MapView.Text(planned, Palette.DimText, 12);
            context.DrawText(plan, new Point(size.Width - Pad - plan.Width, dotY - plan.Height / 2));
            if (row.Row.Km > 0 || i > 0)
            {
                var km = MapView.Text($"km {row.Row.Km:0.0}", Palette.FaintText, 10);
                double kmX = size.Width - Pad - plan.Width - 12 - km.Width;
                if (kmX > x + name.Width + 8)
                    context.DrawText(km, new Point(kmX, dotY - km.Height / 2));
            }

            // What happened there, under it: each part in its colour, on to a second line when long.
            double partX = x, partY = top + 25;
            bool first = true;
            foreach (var (text, brush) in Chips(row, culture))
            {
                var part = MapView.Text(first ? text : "·  " + text, brush, 11);
                if (!first && partX + part.Width > size.Width - Pad)
                {
                    partX = x;
                    partY += 14;
                    part = MapView.Text(text, brush, 11);
                }
                context.DrawText(part, new Point(partX, partY));
                partX += part.Width + 6;
                first = false;
            }
        }
    }

    /// <summary>What happened at a station, each part with its colour.</summary>
    private IEnumerable<(string Text, IBrush Brush)> Chips(StationProgress row, CultureInfo culture)
    {
        switch (row.Outcome)
        {
            case StationOutcome.Ahead:
                if (row.Row.Maintenance)
                    yield return (Strings["TtTechnical"], Palette.FaintText);
                yield break;
            case StationOutcome.Passed:
                yield return (string.Format(culture, Strings["TtPassed"], Clock(row.DepartureClock)), Palette.DimText);
                if (row.DepartureDelay is { } passDelay)
                    yield return (string.Format(culture, Strings["Minutes"], passDelay), DelayBrush(passDelay));
                yield break;
        }
        if (row.ArrivedAt is null)
        {
            yield return (Strings["TtMissed"], Palette.Bad);
            yield break;
        }
        yield return (string.Format(culture, Strings["TtArrived"], Clock(row.ArrivalClock)), Palette.DimText);
        if (row.ArrivalDelay is { } arrival)
            yield return (string.Format(culture, Strings["Minutes"], arrival), DelayBrush(arrival));
        if (row.Outcome == StationOutcome.Missed)
            yield return (Strings["TtLeftEarly"], Palette.Bad);
        else if (row.DepartedAt is not null)
        {
            yield return (string.Format(culture, Strings["TtDeparted"], Clock(row.DepartureClock)), Palette.DimText);
            if (row.DepartureDelay is { } departure)
                yield return (string.Format(culture, Strings["Minutes"], departure), DelayBrush(departure));
        }
        else
            yield return (Strings["TtStanding"], Palette.Station);
        if (row.StopError is { } error)
            yield return Math.Abs(error) < 1
                ? (Strings["TtStopExact"], Palette.Good)
                : (string.Format(culture, Strings[error > 0 ? "TtStopShort" : "TtStopPast"], Math.Abs(error)),
                   Math.Abs(error) <= 5 ? Palette.Good : Math.Abs(error) <= 15 ? Palette.Warn : Palette.Bad);
        if (row.OnPlatform is { } along)
            yield return along
                ? (Strings["TtOnPlatform"], Palette.Good)
                : (string.Format(culture, Strings["TtOffPlatform"], row.OffPlatform ?? 0), Palette.Bad);
    }

    private static IBrush DelayBrush(double? minutes) => Palette.Delay(minutes);

    private static string Minutes(float minutes) =>
        minutes < 0 ? "–" : TimeSpan.FromMinutes(minutes).ToString(@"hh\:mm", CultureInfo.InvariantCulture);

    private static string Clock(double? minutes) =>
        minutes is { } m ? TimeSpan.FromMinutes(m % 1440).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : "–";

    private int RowAt(Point p)
    {
        int i = (int)Math.Floor((p.Y - HeaderHeight) / RowHeight);
        return p.Y >= HeaderHeight && i >= 0 && i < _rows.Count ? i : -1;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int hover = RowAt(e.GetPosition(this));
        if (hover != _hover)
        {
            _hover = hover;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        int i = RowAt(e.GetPosition(this));
        if (i >= 0 && (_rows[i].ArrivedAt ?? _rows[i].DepartedAt) is { } t)
            Seek?.Invoke(Math.Max(0, t - 5));
    }
}
