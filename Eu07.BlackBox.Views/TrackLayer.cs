using System;
using System.Collections.Generic;
using System.Numerics;
using Avalonia;
using Avalonia.Media;

namespace Eu07.BlackBox.Views;

/// <summary>
/// The rails of a scenery made ready to draw fast: cut into square cells so that only those in
/// view are drawn, and kept at a few levels of detail so that from afar a track is drawn with
/// fewer points than there are pixels it covers. The casing takes the switches' ways too, which
/// it is drawn under in one colour; the fill is the plain tracks alone.
/// </summary>
public sealed class TrackLayer
{
    /// <summary>The side of a cell in metres.</summary>
    public const double CellSize = 500;

    /// <summary>The points of a track closer than this to the last kept are left out, by level.</summary>
    public static readonly double[] Tolerances = { 0, 2, 8, 32, 128 };

    /// <summary>A part of the map: what is drawn of it, and where it lies in the map's plane.</summary>
    public sealed record Cell(Rect Bounds, StreamGeometry Casing, StreamGeometry? Fill);

    private readonly List<Cell>[] _levels;

    public TrackLayer(IReadOnlyList<MapTrack> tracks)
    {
        _levels = new List<Cell>[Tolerances.Length];
        for (int level = 0; level < Tolerances.Length; level++)
            _levels[level] = BuildLevel(tracks, Tolerances[level]);
    }

    /// <summary>The level whose left-out points stay within <paramref name="pixels"/> on screen at this scale.</summary>
    public static int LevelFor(double scale, double pixels = 0.75)
    {
        int level = 0;
        for (int i = 1; i < Tolerances.Length; i++)
            if (Tolerances[i] * scale <= pixels)
                level = i;
        return level;
    }

    /// <summary>The cells of the level that reach into <paramref name="view"/>, a rectangle of the map's plane.</summary>
    public IEnumerable<Cell> CellsIn(Rect view, int level)
    {
        foreach (var cell in _levels[Math.Clamp(level, 0, _levels.Length - 1)])
            if (cell.Bounds.Intersects(view))
                yield return cell;
    }

    public int CellCount(int level) => _levels[level].Count;

    /// <summary>The points of a track with those nearer than <paramref name="tolerance"/> to the last kept left out; its ends stay.</summary>
    public static List<Vector3> Simplified(IReadOnlyList<Vector3> points, double tolerance)
    {
        var kept = new List<Vector3>(points.Count);
        if (points.Count == 0)
            return kept;
        kept.Add(points[0]);
        double square = tolerance * tolerance;
        for (int i = 1; i < points.Count - 1; i++)
        {
            var last = kept[^1];
            double dx = points[i].X - last.X, dz = points[i].Z - last.Z;
            if (dx * dx + dz * dz >= square)
                kept.Add(points[i]);
        }
        if (points.Count > 1)
            kept.Add(points[^1]);
        return kept;
    }

    private sealed class Builder
    {
        public readonly List<List<Point>> Casing = new();
        public readonly List<List<Point>> Fill = new();
        public double Left = double.MaxValue, Top = double.MaxValue, Right = double.MinValue, Bottom = double.MinValue;

        public void Grow(Point p)
        {
            Left = Math.Min(Left, p.X);
            Right = Math.Max(Right, p.X);
            Top = Math.Min(Top, p.Y);
            Bottom = Math.Max(Bottom, p.Y);
        }
    }

    private static List<Cell> BuildLevel(IReadOnlyList<MapTrack> tracks, double tolerance)
    {
        var cells = new Dictionary<(long, long), Builder>();
        foreach (var track in tracks)
        {
            if (track.Points.Count < 2)
                continue;
            bool fill = track.Kind != TrackKind.SwitchBranch;
            var points = tolerance > 0 ? Simplified(track.Points, tolerance) : (IReadOnlyList<Vector3>)track.Points;

            // A run of the track's points in one cell; a segment over a border goes with the cell it ends in,
            // which starts from the point before, so its bounds cover the whole segment.
            List<Point>? casingRun = null, fillRun = null;
            (long, long) current = default;
            for (int i = 0; i < points.Count; i++)
            {
                var p = Plane(points[i]);
                var key = ((long)Math.Floor(p.X / CellSize), (long)Math.Floor(p.Y / CellSize));
                if (casingRun is null || key != current)
                {
                    var builder = Of(cells, key);
                    var start = casingRun is null ? (Point?)null : casingRun[^1];
                    casingRun = new List<Point>();
                    builder.Casing.Add(casingRun);
                    fillRun = null;
                    if (fill)
                    {
                        fillRun = new List<Point>();
                        builder.Fill.Add(fillRun);
                    }
                    if (start is { } joined)
                    {
                        // The segment from the last cell starts there, so the line has no gap at the border.
                        casingRun.Add(joined);
                        fillRun?.Add(joined);
                        builder.Grow(joined);
                    }
                    current = key;
                }
                casingRun.Add(p);
                fillRun?.Add(p);
                Of(cells, key).Grow(p);
            }
        }

        var result = new List<Cell>(cells.Count);
        foreach (var builder in cells.Values)
        {
            var casing = Geometry(builder.Casing);
            if (casing is null)
                continue;
            var bounds = new Rect(new Point(builder.Left, builder.Top), new Point(builder.Right, builder.Bottom));
            result.Add(new Cell(bounds, casing, Geometry(builder.Fill)));
        }
        return result;
    }

    private static Builder Of(Dictionary<(long, long), Builder> cells, (long, long) key)
    {
        if (!cells.TryGetValue(key, out var builder))
            cells[key] = builder = new Builder();
        return builder;
    }

    private static StreamGeometry? Geometry(List<List<Point>> runs)
    {
        StreamGeometry? geometry = null;
        StreamGeometryContext? g = null;
        try
        {
            foreach (var run in runs)
            {
                if (run.Count < 2)
                    continue;
                if (geometry is null)
                {
                    geometry = new StreamGeometry();
                    g = geometry.Open();
                }
                g!.BeginFigure(run[0], false);
                for (int i = 1; i < run.Count; i++)
                    g.LineTo(run[i]);
                g.EndFigure(false);
            }
        }
        finally
        {
            g?.Dispose();
        }
        return geometry;
    }

    // As MapView draws the plane: x to the left, z up the screen.
    private static Point Plane(Vector3 p) => new(-p.X, -p.Z);
}
