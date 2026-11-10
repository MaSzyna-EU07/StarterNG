using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Eu07.BlackBox.Views;

/// <summary>
/// The map of the scenery at a moment of the recording: the rail tracks with the switches
/// as they lay, the signals in the colour of what they showed, every vehicle where it was,
/// and the way the user's train went, coloured by how its speed stood against the AI's.
/// North is up, as on the map in the simulator. Dragged to move, the wheel zooms, a
/// double click shows it all.
/// </summary>
public sealed class MapView : Control
{
    private const double MinScale = 0.002;
    private const double MaxScale = 40;
    private const double FollowScale = 1.2;

    private Recording? _recording;
    private int _builtRevision = -1;
    private int _builtTracks = -1;
    private StreamGeometry? _tracks;
    private readonly List<(int Index, int Branch, StreamGeometry Way)> _switchWays = new();
    private readonly StreamGeometry?[] _trail = new StreamGeometry?[5];
    private Rect _world;

    private double _centerX, _centerZ;
    private double _scale = 0.2; // pixels per metre
    private bool _fitted;

    private Point? _dragFrom;
    private (double X, double Z) _dragCenter;
    private Point? _pointer;
    private double _time;
    private bool _follow;

    /// <summary>The user moved the map away from the train: following is off.</summary>
    public event Action? FollowStopped;

    public MapView()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public Recording? Recording
    {
        get => _recording;
        set
        {
            _recording = value;
            _builtRevision = -1;
            _builtTracks = -1;
            _fitted = false;
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

    /// <summary>Keeps the user's train in the middle.</summary>
    public bool Follow
    {
        get => _follow;
        set
        {
            // Close enough to see the train go, unless already closer.
            if (value && !_follow)
                _scale = Math.Max(_scale, FollowScale);
            _follow = value;
            InvalidateVisual();
        }
    }

    public IBlackBoxStrings Strings { get; set; } = new DefaultBlackBoxStrings();

    /// <summary>Closer by the factor given, about the middle.</summary>
    public void ZoomBy(double factor)
    {
        _scale = Math.Clamp(_scale * factor, MinScale, MaxScale);
        InvalidateVisual();
    }

    /// <summary>The map as a whole in view.</summary>
    public void Fit()
    {
        FitWorld();
        InvalidateVisual();
    }

    // Also from the render, which must not ask for another one.
    private void FitWorld()
    {
        if (_world.Width <= 0 && _world.Height <= 0)
            return;
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;
        _centerX = -_world.Center.X;
        _centerZ = -_world.Center.Y;
        _scale = Math.Clamp(Math.Min(size.Width / Math.Max(_world.Width, 50), size.Height / Math.Max(_world.Height, 50)) * 0.92,
                            MinScale, MaxScale);
        if (_follow)
            _scale = Math.Max(_scale, FollowScale);
        _fitted = true;
    }

    // The map's plane as the simulator's map draws it, seen from above: x to the left (the game's x runs the
    // other way than a map's east), z up the screen.
    private static Point Plane(double x, double z) => new(-x, -z);

    private Point ToScreen(double x, double z) =>
        new((_centerX - x) * _scale + Bounds.Width / 2, (_centerZ - z) * _scale + Bounds.Height / 2);

    private (double X, double Z) ToWorld(Point screen) =>
        (_centerX - (screen.X - Bounds.Width / 2) / _scale, _centerZ - (screen.Y - Bounds.Height / 2) / _scale);

    private void Build(Recording recording)
    {
        if (recording.Tracks.Count != _builtTracks)
        {
            _builtTracks = recording.Tracks.Count;
            _switchWays.Clear();
            var tracks = new StreamGeometry();
            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
            using (var g = tracks.Open())
            {
                foreach (var track in recording.Tracks)
                {
                    if (track.Points.Count < 2)
                        continue;
                    foreach (var p in track.Points)
                    {
                        left = Math.Min(left, -p.X);
                        right = Math.Max(right, -p.X);
                        top = Math.Min(top, -p.Z);
                        bottom = Math.Max(bottom, -p.Z);
                    }
                    if (track.Kind == TrackKind.SwitchBranch)
                    {
                        var way = new StreamGeometry();
                        using (var w = way.Open())
                            Polyline(w, track.Points);
                        _switchWays.Add((track.Index, track.Branch, way));
                    }
                    else
                        Polyline(g, track.Points);
                }
            }
            _tracks = tracks;
            _world = left <= right ? new Rect(new Point(left, top), new Point(right, bottom)) : default;
        }
        BuildTrail(recording);
        _builtRevision = recording.Revision;
    }

    private static void Polyline(StreamGeometryContext g, IReadOnlyList<System.Numerics.Vector3> points)
    {
        g.BeginFigure(Plane(points[0].X, points[0].Z), false);
        for (int i = 1; i < points.Count; i++)
            g.LineTo(Plane(points[i].X, points[i].Z));
        g.EndFigure(false);
    }

    /// <summary>The way the user's train went, a geometry for each judgement of its speed.</summary>
    private void BuildTrail(Recording recording)
    {
        var lines = new List<List<Point>>[_trail.Length];
        for (int i = 0; i < lines.Length; i++)
            lines[i] = new List<List<Point>>();
        var cab = recording.Cab;
        int open = -1;
        for (int i = 1; i < cab.Count; i++)
        {
            var before = cab[i - 1];
            var sample = cab[i];
            if (sample.Vehicle != before.Vehicle || sample.Time - before.Time > 1.0)
            {
                open = -1;
                continue;
            }
            if (before.Position.DistanceTo(sample.Position) < 0.5 && open >= 0)
                continue;
            int kind = (int)DrivingSummary.Judge(sample);
            if (kind != open)
            {
                lines[kind].Add(new List<Point> { Plane(before.Position.X, before.Position.Z) });
                open = kind;
            }
            lines[kind][^1].Add(Plane(sample.Position.X, sample.Position.Z));
        }
        for (int kind = 0; kind < _trail.Length; kind++)
        {
            if (lines[kind].Count == 0)
            {
                _trail[kind] = null;
                continue;
            }
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                foreach (var line in lines[kind].Where(l => l.Count > 1))
                {
                    g.BeginFigure(line[0], false);
                    foreach (var p in line.Skip(1))
                        g.LineTo(p);
                    g.EndFigure(false);
                }
            }
            _trail[kind] = geometry;
        }
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        context.FillRectangle(Palette.MapBackground, new Rect(size));
        var recording = _recording;
        if (recording is null)
            return;
        if (recording.Revision != _builtRevision)
            Build(recording);
        if (!_fitted)
            FitWorld();
        if (_follow && recording.CabPositionAt(_time) is { } followed)
        {
            _centerX = followed.X;
            _centerZ = followed.Z;
        }

        var toScreen = Matrix.CreateTranslation(_centerX, _centerZ) * Matrix.CreateScale(_scale, _scale) *
                       Matrix.CreateTranslation(size.Width / 2, size.Height / 2);
        double pixel = 1 / _scale;
        using (context.PushTransform(toScreen))
        {
            // The tracks on a dark casing, so they stand out where they cross and run close.
            var casing = new Pen(Palette.TrackCasing, 5 * pixel, lineCap: PenLineCap.Round);
            if (_tracks is not null)
                context.DrawGeometry(null, casing, _tracks);
            foreach (var (_, _, way) in _switchWays)
                context.DrawGeometry(null, casing, way);
            if (_tracks is not null)
                context.DrawGeometry(null, new Pen(Palette.Track, 1.7 * pixel, lineCap: PenLineCap.Round), _tracks);
            var off = new Pen(Palette.SwitchOff, 1.7 * pixel, lineCap: PenLineCap.Round);
            var on = new Pen(Palette.Track, 1.7 * pixel, lineCap: PenLineCap.Round);
            // The ways not taken under those taken.
            foreach (var (index, branch, way) in _switchWays)
            {
                int state = recording.SwitchAt(index, _time);
                if (state >= 0 && state != branch)
                    context.DrawGeometry(null, off, way);
            }
            foreach (var (index, branch, way) in _switchWays)
            {
                int state = recording.SwitchAt(index, _time);
                if (state < 0 || state == branch)
                    context.DrawGeometry(null, on, way);
            }
            // The way the train went, in the colour of the judgement.
            for (int kind = 0; kind < _trail.Length; kind++)
                if (_trail[kind] is { } trail)
                    context.DrawGeometry(null, new Pen(Palette.Of((SpeedJudgement)kind), 3.5 * pixel, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), trail);
        }

        DrawStopPoints(context, recording);
        DrawHornPoints(context, recording);
        DrawSignals(context, recording);
        DrawPassings(context, recording);
        DrawVehicles(context, recording);
        DrawUserTrain(context, recording);
        DrawCamera(context, recording);
        DrawScale(context, size);
        DrawLegend(context, size);
        DrawHover(context, recording);
    }

    private bool Visible(Point p, double margin = 20) =>
        p.X >= -margin && p.Y >= -margin && p.X <= Bounds.Width + margin && p.Y <= Bounds.Height + margin;

    private void DrawSignals(DrawingContext context, Recording recording)
    {
        double radius = _scale > 0.5 ? 4.5 : _scale > 0.1 ? 3.5 : 2.5;
        var outline = new Pen(Palette.Background, 1);
        foreach (var signal in recording.Signals)
        {
            var p = ToScreen(signal.Position.X, signal.Position.Z);
            if (!Visible(p))
                continue;
            context.DrawEllipse(Palette.Of(recording.AspectAt(signal.Index, _time)), outline, p, radius, radius);
        }
    }

    /// <summary>
    /// The stop points (W4): a white plate, blue where the user's train stopped, red where it
    /// missed the stop; the name of the station once close enough to read it.
    /// </summary>
    // The way the trains a stop point is for go: a wedge ahead of the plate, as a train sees the W4 coming.
    private static void DrawStopWay(DrawingContext context, Point plate, float heading, double size, IBrush fill, IPen outline)
    {
        // the map shows x to the left, z up: a heading from +z towards +x turns up the screen and to the left
        var ahead = new Vector(-Math.Sin(heading), -Math.Cos(heading));
        var across = new Vector(-ahead.Y, ahead.X);
        var tip = plate + ahead * (size * 4.2);
        var root = plate + ahead * (size * 1.8);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(tip, true);
            g.LineTo(root + across * (size * 1.3));
            g.LineTo(root - across * (size * 1.3));
            g.EndFigure(true);
        }
        context.DrawGeometry(fill, outline, geometry);
    }

    private void DrawStopPoints(DrawingContext context, Recording recording)
    {
        if (recording.StopPoints.Count == 0)
            return;
        var outcomes = new Dictionary<(double, double), StationKind>();
        foreach (var station in recording.UserStations())
        {
            if (station.Time > _time || station.Kind is not (StationKind.Stopped or StationKind.Missed))
                continue;
            var key = (Math.Round(station.Position.X, 1), Math.Round(station.Position.Z, 1));
            if (!outcomes.TryGetValue(key, out var was) || was != StationKind.Missed)
                outcomes[key] = station.Kind;
        }
        // Close up every plate is named; from afar each station once, where the names don't cover each other.
        bool near = _scale > 0.35;
        double size = _scale > 0.5 ? 4 : 3;
        var outline = new Pen(Palette.MapBackground, 1);
        var labels = new List<(Point At, string Name, IBrush Fill)>();
        foreach (var point in recording.StopPoints)
        {
            var p = ToScreen(point.Position.X, point.Position.Z);
            if (!Visible(p))
                continue;
            var fill = outcomes.TryGetValue((Math.Round(point.Position.X, 1), Math.Round(point.Position.Z, 1)), out var kind)
                ? kind == StationKind.Missed ? Palette.Bad : Palette.Station
                : Palette.StopPoint;
            var plate = new Rect(p.X - size, p.Y - size * 1.3, size * 2, size * 2.6);
            // a W32 hollow, in the colour a W4 would be filled with
            if (point.ForShortTrains)
                context.DrawRectangle(Palette.MapBackground, new Pen(fill, 1.5), plate, 1.5, 1.5);
            else
                context.DrawRectangle(fill, outline, plate, 1.5, 1.5);
            if (point.HasHeading)
                DrawStopWay(context, p, point.Heading, size, fill, outline);
            labels.Add((p, StationNames.Display(point.Station), fill));
        }
        var delays = StationProgress.StationDelays(recording, _time);
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var taken = new List<Rect>();
        // The label of the user's train stays clear.
        if (recording.CabPositionAt(_time) is { } train)
        {
            var mark = ToScreen(train.X, train.Z);
            taken.Add(new Rect(mark.X - 10, mark.Y - 24, 170, 34));
        }
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        foreach (var (p, name, fill) in labels)
        {
            if (!near && !named.Add(name))
                continue;
            var text = Text(name, Palette.Text, near ? 11 : 10);
            FormattedText? late = delays.TryGetValue(name, out var minutes)
                ? Text(string.Format(culture, Strings["Minutes"], minutes), Palette.Delay(minutes), near ? 11 : 10)
                : null;
            double width = text.Width + (late is null ? 0 : late.Width + 6) + 8;
            var box = new Rect(p.X + size + 3, p.Y - text.Height / 2 - 1, width, text.Height + 2);
            if (taken.Exists(r => r.Inflate(2).Intersects(box)))
                continue;
            taken.Add(box);
            context.DrawRectangle(Palette.Faded(Palette.MapBackground, 0.8), null, box);
            context.DrawText(text, new Point(box.X + 4, box.Y + 1));
            if (late is not null)
                context.DrawText(late, new Point(box.X + 4 + text.Width + 6, box.Y + 1));
        }
    }

    /// <summary>The horn points (W6): a diamond, green where the user sounded the horn, red where not.</summary>
    private void DrawHornPoints(DrawingContext context, Recording recording)
    {
        if (recording.HornPoints.Count == 0)
            return;
        var checks = recording.UserHornChecks().Where(c => c.Request.Time <= _time).ToList();
        double size = _scale > 0.5 ? 5 : 3.5;
        var outline = new Pen(Palette.MapBackground, 1);
        foreach (var point in recording.HornPoints)
        {
            var p = ToScreen(point.Position.X, point.Position.Z);
            if (!Visible(p))
                continue;
            var check = checks.LastOrDefault(c => c.Request.Position.DistanceTo(point.Position) < 1);
            var fill = check is null ? Palette.HornPoint : check.Sounded ? Palette.Good : Palette.Bad;
            context.DrawGeometry(fill, outline, Diamond(p, size));
        }
    }

    private static StreamGeometry Diamond(Point p, double size)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(p + new Vector(0, -size), true);
            g.LineTo(p + new Vector(size, 0));
            g.LineTo(p + new Vector(0, size));
            g.LineTo(p + new Vector(-size, 0));
            g.EndFigure(true);
        }
        return geometry;
    }

    /// <summary>Where the user looked from, when not from the cab: a dot with a wedge of where it looked.</summary>
    private void DrawCamera(DrawingContext context, Recording recording)
    {
        if (recording.CameraAt(_time) is not { } camera || camera.Mode == CameraMode.Cab)
            return;
        var p = ToScreen(camera.Position.X, camera.Position.Z);
        if (!Visible(p, 60))
            return;
        // The heading from +z towards +x: up the screen and to the left.
        const double reach = 46, half = 0.45;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(p, true);
            g.LineTo(p + new Vector(-Math.Sin(camera.Heading - half), -Math.Cos(camera.Heading - half)) * reach);
            g.ArcTo(p + new Vector(-Math.Sin(camera.Heading + half), -Math.Cos(camera.Heading + half)) * reach, new Size(reach, reach), 0,
                    false, SweepDirection.CounterClockwise);
            g.EndFigure(true);
        }
        context.DrawGeometry(Palette.Faded(Palette.HornPoint, 0.18), new Pen(Palette.Faded(Palette.HornPoint, 0.5), 1), geometry);
        context.DrawEllipse(Palette.HornPoint, new Pen(Palette.MapBackground, 1.5), p, 4.5, 4.5);
    }

    /// <summary>Where the user's train passed signals; those at danger ringed red.</summary>
    private void DrawPassings(DrawingContext context, Recording recording)
    {
        var passed = new Pen(Palette.Within, 1.5);
        var danger = new Pen(Palette.Over, 2.5);
        foreach (var passing in recording.UserPassings())
        {
            var p = ToScreen(passing.Position.X, passing.Position.Z);
            if (!Visible(p))
                continue;
            if (passing.AtDanger)
                context.DrawEllipse(null, danger, p, 10, 10);
            else if (passing.Time <= _time)
                context.DrawEllipse(null, passed, p, 7, 7);
        }
    }

    private void DrawVehicles(DrawingContext context, Recording recording)
    {
        int user = recording.CabAt(_time) is { } sample && _time - sample.Time < 1.5 ? sample.Vehicle : 0;
        foreach (var state in recording.VehiclesAt(_time))
        {
            var info = recording.Vehicle(state.Id);
            var brush = state.Id == user ? Palette.UserTrain : state.Driving == Driver.Ai ? Palette.VehicleAi : Palette.Vehicle;
            DrawVehicle(context, state.Position.X, state.Position.Z, state.Heading, info?.Length ?? 15, info?.Width ?? 3, brush);
        }
    }

    private void DrawVehicle(DrawingContext context, double x, double z, double heading, double length, double width,
                             IBrush brush)
    {
        var center = ToScreen(x, z);
        if (!Visible(center, length * _scale + 10))
            return;
        // The front from +z towards +x: up the screen and to the left.
        double fx = Math.Sin(heading), fz = Math.Cos(heading);
        double half = Math.Max(length * _scale, 5) / 2, side = Math.Max(width * _scale, 3) / 2;
        var front = new Vector(-fx, -fz);
        var left = new Vector(-fz, fx);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(center + front * half + left * side, true);
            g.LineTo(center + front * half - left * side);
            g.LineTo(center - front * half - left * side);
            g.LineTo(center - front * half + left * side);
            g.EndFigure(true);
        }
        context.DrawGeometry(brush, new Pen(Palette.Background, 0.8), geometry);
    }

    /// <summary>The head of the user's train between its samples, ten a second, on top of everything.</summary>
    private void DrawUserTrain(DrawingContext context, Recording recording)
    {
        if (recording.CabPositionAt(_time) is not { } position)
            return;
        var p = ToScreen(position.X, position.Z);
        context.DrawEllipse(Palette.MapBackground, new Pen(Palette.UserTrain, 2), p, 7, 7);
        context.DrawEllipse(Palette.UserTrain, null, p, 3, 3);
        // The train and how late it is, by its mark.
        if (StationProgress.DelayAt(recording, _time) is not { } delay)
            return;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var train = Text(delay.Train, Palette.UserTrain, 11, FontWeight.SemiBold);
        var late = Text(Math.Abs(delay.Minutes) < 0.5 ? Strings["OnTime"] : string.Format(culture, Strings["Minutes"], delay.Minutes),
                        Palette.Delay(delay.Minutes), 11, FontWeight.SemiBold);
        var box = TrainLabel(p, train, late);
        context.DrawRectangle(Palette.Faded(Palette.MapBackground, 0.85), null, box);
        context.DrawText(train, new Point(box.X + 4, box.Y + 2));
        context.DrawText(late, new Point(box.X + train.Width + 12, box.Y + 2));
    }

    /// <summary>Where the label of the user's train goes, by its mark.</summary>
    private static Rect TrainLabel(Point mark, FormattedText train, FormattedText late) =>
        new(mark.X + 12, mark.Y - 22, train.Width + late.Width + 16, train.Height + 4);

    private void DrawScale(DrawingContext context, Size size)
    {
        double metres = Nice(120 / _scale);
        double length = metres * _scale;
        var y = size.Height - 14;
        var pen = new Pen(Palette.DimText, 1.5);
        context.DrawLine(pen, new Point(size.Width - 16 - length, y), new Point(size.Width - 16, y));
        context.DrawLine(pen, new Point(size.Width - 16 - length, y - 4), new Point(size.Width - 16 - length, y + 1));
        context.DrawLine(pen, new Point(size.Width - 16, y - 4), new Point(size.Width - 16, y + 1));
        var text = Text(metres >= 1000 ? $"{metres / 1000:0.#} km" : $"{metres:0} m", Palette.DimText, 11);
        context.DrawText(text, new Point(size.Width - 16 - length / 2 - text.Width / 2, y - 18));
    }

    private void DrawLegend(DrawingContext context, Size size)
    {
        var rows = new (IBrush Brush, string Text)[]
        {
            (Palette.Within, Strings["LegendWithin"]),
            (Palette.Slightly, string.Format(CultureInfo.CurrentCulture, Strings["LegendSlightly"], DrivingSummary.DefaultTolerance)),
            (Palette.Over, Strings["LegendOver"]),
            (Palette.Autopilot, Strings["LegendAutopilot"]),
            (Palette.NoData, Strings["LegendNoData"])
        };
        var marks = new List<(IBrush Brush, string Text, int Shape)>();
        if (_recording is { } r)
        {
            if (r.StopPoints.Any(point => !point.ForShortTrains))
                marks.Add((Palette.StopPoint, Strings["LegendStopPoint"], 0));
            if (r.StopPoints.Any(point => point.ForShortTrains))
                marks.Add((Palette.StopPoint, Strings["LegendShortStop"], 3));
            if (r.HornPoints.Count > 0)
                marks.Add((Palette.HornPoint, Strings["LegendHorn"], 1));
            if (r.Cameras.Count > 0)
                marks.Add((Palette.HornPoint, Strings["LegendCamera"], 2));
        }
        var texts = rows.Select(row => Text(row.Text, Palette.DimText, 11)).Concat(marks.Select(m => Text(m.Text, Palette.DimText, 11))).ToList();
        const double line = 17;
        double width = texts.Max(t => t.Width) + 44, height = texts.Count * line + 14 + (marks.Count > 0 ? 6 : 0);
        var box = new Rect(10, size.Height - height - 10, width, height);
        context.DrawRectangle(Palette.Faded(Palette.Card, 0.9), new Pen(Palette.CardBorder, 1), box);
        double y = box.Top + 7;
        for (int i = 0; i < rows.Length; i++, y += line)
        {
            context.DrawLine(new Pen(rows[i].Brush, 4, lineCap: PenLineCap.Round), new Point(box.Left + 12, y + 8), new Point(box.Left + 26, y + 8));
            context.DrawText(texts[i], new Point(box.Left + 34, y));
        }
        if (marks.Count > 0)
        {
            context.DrawLine(new Pen(Palette.Grid, 1), new Point(box.Left + 10, y + 3), new Point(box.Right - 10, y + 3));
            y += 6;
        }
        for (int i = 0; i < marks.Count; i++, y += line)
        {
            var c = new Point(box.Left + 19, y + 8);
            if (marks[i].Shape == 0)
                context.DrawRectangle(marks[i].Brush, null, new Rect(c.X - 3, c.Y - 4, 6, 8), 1.5, 1.5);
            else if (marks[i].Shape == 3)
                context.DrawRectangle(null, new Pen(marks[i].Brush, 1.5), new Rect(c.X - 3, c.Y - 4, 6, 8), 1.5, 1.5);
            else if (marks[i].Shape == 1)
                context.DrawGeometry(marks[i].Brush, null, Diamond(c, 4.5));
            else
                context.DrawEllipse(marks[i].Brush, null, c, 4, 4);
            context.DrawText(texts[rows.Length + i], new Point(box.Left + 34, y));
        }
    }

    /// <summary>The name of the signal or the vehicle under the pointer.</summary>
    private void DrawHover(DrawingContext context, Recording recording)
    {
        if (_pointer is not { } pointer || _dragFrom is not null)
            return;
        string? label = null;
        double best = 10;
        foreach (var signal in recording.Signals)
        {
            double d = Distance(ToScreen(signal.Position.X, signal.Position.Z), pointer);
            if (d < best)
            {
                best = d;
                var aspect = recording.AspectAt(signal.Index, _time);
                label = aspect is { } a ? $"{signal.Name} · {AspectText(Strings, a.Speed1, a.Speed2)}" : signal.Name;
            }
        }
        var culture = CultureInfo.CurrentCulture;
        foreach (var point in recording.StopPoints)
        {
            double d = Distance(ToScreen(point.Position.X, point.Position.Z), pointer);
            if (d < best)
            {
                best = d;
                label = string.Format(culture, Strings[point.ForShortTrains ? "ShortStopHover" : "StopPointHover"], StationNames.Display(point.Station));
                if (point.Platform > 0)
                    label += " · " + string.Format(culture, Strings["PlatformHover"], point.PlatformLength);
                else if (point.Platform < 0)
                    label += " · " + string.Format(culture, Strings["ShortTrainsHover"], point.PlatformLength);
                // the preferred place to stop, as the driver reads it: the head, or the middle of the train
                if (point.StopAt < 0)
                    label += " · " + string.Format(culture, Strings["StopHeadHover"], -point.StopAt);
                else if (point.StopAt > 0)
                    label += " · " + string.Format(culture, Strings["StopMiddleHover"], point.StopAt);
            }
        }
        foreach (var point in recording.HornPoints)
        {
            double d = Distance(ToScreen(point.Position.X, point.Position.Z), pointer);
            if (d < best)
            {
                best = d;
                label = string.Format(culture, Strings["HornHover"], point.Name, point.Duration);
            }
        }
        foreach (var state in recording.VehiclesAt(_time))
        {
            double d = Distance(ToScreen(state.Position.X, state.Position.Z), pointer);
            if (d < best && recording.Vehicle(state.Id) is { } info)
            {
                best = d;
                label = string.IsNullOrEmpty(info.Train)
                    ? $"{info.Name} · {Math.Abs(state.SpeedKmh):0} km/h"
                    : $"{info.Name} ({info.Train}) · {Math.Abs(state.SpeedKmh):0} km/h";
            }
        }
        if (label is null)
            return;
        var text = Text(label, Palette.Text, 12);
        var box = new Rect(pointer.X + 14, pointer.Y - 12 - text.Height, text.Width + 16, text.Height + 8);
        context.DrawRectangle(Palette.Faded(Palette.Card, 0.95), new Pen(Palette.CardBorder, 1), box, 6, 6);
        context.DrawText(text, new Point(box.Left + 8, box.Top + 4));
    }

    /// <summary>An aspect as the speeds it gives: stop, a speed, or go.</summary>
    public static string AspectText(IBlackBoxStrings strings, float speed1, float speed2)
    {
        if (speed1 == 0)
            return strings["AspectStop"];
        if (speed1 < 0 && speed2 < 0)
            return strings["AspectFree"];
        string first = speed1 < 0 ? "∞" : speed1.ToString("0", CultureInfo.InvariantCulture);
        string second = speed2 < 0 ? "∞" : speed2.ToString("0", CultureInfo.InvariantCulture);
        return $"{first}/{second}";
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double Nice(double value)
    {
        double power = Math.Pow(10, Math.Floor(Math.Log10(value)));
        double unit = value / power;
        return (unit < 2 ? 1 : unit < 5 ? 2 : 5) * power;
    }

    internal static FormattedText Text(string text, IBrush brush, double size, FontWeight weight = FontWeight.Normal) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            weight == FontWeight.Normal ? Typeface.Default : new Typeface(Typeface.Default.FontFamily, FontStyle.Normal, weight),
            size, brush);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
            return;
        if (e.ClickCount == 2)
        {
            if (_follow)
                StopFollowing();
            Fit();
            return;
        }
        _dragFrom = point.Position;
        _dragCenter = (_centerX, _centerZ);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        _pointer = position;
        if (_dragFrom is { } from)
        {
            var moved = position - from;
            if (_follow && (Math.Abs(moved.X) > 3 || Math.Abs(moved.Y) > 3))
                StopFollowing();
            if (!_follow)
            {
                _centerX = _dragCenter.X + moved.X / _scale;
                _centerZ = _dragCenter.Z + moved.Y / _scale;
            }
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragFrom = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointer = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var at = e.GetPosition(this);
        var (x, z) = ToWorld(at);
        _scale = Math.Clamp(_scale * Math.Pow(1.25, e.Delta.Y), MinScale, MaxScale);
        if (!_follow)
        {
            // The place under the pointer stays under it.
            _centerX = x + (at.X - Bounds.Width / 2) / _scale;
            _centerZ = z + (at.Y - Bounds.Height / 2) / _scale;
        }
        InvalidateVisual();
        e.Handled = true;
    }

    private void StopFollowing()
    {
        _follow = false;
        FollowStopped?.Invoke();
    }
}
