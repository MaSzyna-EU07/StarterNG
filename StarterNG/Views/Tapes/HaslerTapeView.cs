using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using StarterNG.Domain.Tapes;

namespace StarterNG.Views.Tapes;

/// <summary>
/// A trip drawn as a Hasler tape: blue-grey waxed paper, on which every record is
/// a light, slightly fuzzy line.
/// <list type="bullet">
/// <item>Top margin: a hole every 0.5 km, with the full hours punched in dots between them.</item>
/// <item>One field ruled every 10 km/h. The speed curve runs in the lower part and its
/// figures sit on their lines across the paper. The minute stylus zig-zags in the
/// upper part on a 10 minute cycle, peaking on every tenth minute. While the train
/// stands it keeps going, so a stop on a distance-fed tape shows as a brush of strokes.</item>
/// <item>Two red lines. Every vigilance (SHP) acknowledgement is a stroke up through the upper one.</item>
/// <item>Motor current: a step of its own stylus.</item>
/// <item>Brake cylinder pressure: spikes rising from a baseline near the bottom edge.</item>
/// </list>
/// The paper is fed by distance or by time.
/// </summary>
public sealed class HaslerTapeView : TapeView
{
    public HaslerTapeView() => RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.LowQuality);

    private const double Pitch = 16; // px between printed lines, one per 10 km/h
    private const int MinuteLines = 6; // the minute stylus swings over this many lines

    private const double TopEdge = 26;
    private const double FieldTop = TopEdge + 10;
    private const double ShpGap = 14;
    private const double ShpReach = 9;
    private const double RedGap = 22;
    private const double CurrentBand = 16;
    private const double BrakeBand = 46;
    private const double BottomEdge = 20;

    private const double MinuteCycle = 600; // s, from one peak to the next
    private const double PerforationStepM = 500;
    private const double BrakeFullScaleBar = 5;
    private const double LabelSpacing = 170;

    private const int GrainSize = 512;
    private const int GrainPatch = 256; // px laid at a time, each from its own place in the grain
    private const int MottlingCell = 8; // px of paper to a pixel of the mottling

    private static readonly IBrush PaperBrush = new SolidColorBrush(Color.Parse("#7A94A5"));
    private static readonly Bitmap Grain = GrainTexture();
    private static Bitmap? _mottling;
    private static readonly IBrush EdgeBrush = new SolidColorBrush(Color.Parse("#A8B9C3"));
    private static readonly IBrush PrintBrush = new SolidColorBrush(Color.Parse("#2A2B33"));
    private static readonly IBrush HoleBrush = new SolidColorBrush(Color.Parse("#EEF3F5"));
    private static readonly IPen PrintPen = new Pen(PrintBrush, 1);
    private static readonly IPen EdgePen = new Pen(PrintBrush, 1.2);
    private static readonly IPen RedPen = new Pen(new SolidColorBrush(Color.Parse("#9C4E63")), 4);
    private static readonly IPen StylusPen = new Pen(new SolidColorBrush(Color.Parse("#E4EDF2"), 0.9), 1.5,
                                                     lineJoin: PenLineJoin.Round, lineCap: PenLineCap.Round);
    private static readonly IPen StylusHalo = new Pen(new SolidColorBrush(Color.Parse("#E4EDF2"), 0.22), 3.6,
                                                      lineJoin: PenLineJoin.Round, lineCap: PenLineCap.Round);
    private static readonly Typeface PrintFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    // 5x7 dot figures for the hours punched into the top edge.
    private static readonly string[] Digits =
    {
        "01110100011001110101110011000101110", "00100011000010000100001000010001110",
        "01110100010000100010001000100011111", "11111000100010000010000011000101110",
        "00010001100101010010111110001000010", "11111100001111000001000011000101110",
        "00110010001000011110100011000101110", "11111000010001000100010000100001000",
        "01110100011000101110100011000101110", "01110100011000101111000010001001100"
    };

    protected override double LeftMargin => 8;

    private int SpeedLines => (int)(TopSpeed / 10);

    private double FieldBottom => FieldTop + (SpeedLines + MinuteLines) * Pitch;

    private double RedTop => FieldBottom + ShpGap;

    private double CurrentTop => RedTop + RedGap + 4;

    private double BrakeBase => CurrentTop + CurrentBand + BrakeBand;

    protected override double TapeHeight => BrakeBase + 6 + BottomEdge;

    private double SpeedY(double speed) => FieldBottom - Math.Min(speed, TopSpeed) / 10 * Pitch;

    /// <summary>The minute stylus: at the top on every tenth minute, at the bottom five minutes later.</summary>
    private static double MinuteY(double clock)
    {
        double phase = clock % MinuteCycle / MinuteCycle;
        return FieldTop + MinuteLines * Pitch * (1 - Math.Abs(1 - 2 * phase));
    }

    private double BrakeY(double press) => BrakeBase - BrakeBand * Math.Clamp(press / BrakeFullScaleBar, 0, 1);

    protected override void DrawBlank(DrawingContext context)
    {
        double width = Bounds.Width;
        double height = TapeHeight;
        DrawPaper(context, width, height);
        context.DrawRectangle(EdgeBrush, null, new Rect(0, 0, width, TopEdge));
        context.DrawRectangle(EdgeBrush, null, new Rect(0, height - BottomEdge, width, BottomEdge));
        context.DrawLine(EdgePen, new Point(0, height - BottomEdge), new Point(width, height - BottomEdge));
    }

    protected override void DrawTape(DrawingContext context, HaslerTape tape)
    {
        DrawPrint(context, Bounds.Width);
        DrawEdges(context, tape, TapeHeight);
        DrawStylus(context, tape, s => MinuteY(ClockAt(s)));
        DrawStylus(context, tape, s => SpeedY(s.SpeedKmh));
        DrawShp(context, tape);
        DrawStylus(context, tape, s => s.Current ? CurrentTop + CurrentBand - 4 : CurrentTop + 3);
        DrawStylus(context, tape, s => BrakeY(s.BrakePressBar));
    }

    /// <summary>
    /// What the paper comes printed with: a line every 10 km/h across the whole
    /// field, the speed figures climbing across it - each on its own line, which
    /// breaks around it - and the two red lines.
    /// </summary>
    private void DrawPrint(DrawingContext context, double width)
    {
        int lines = SpeedLines + MinuteLines;
        var gaps = new System.Collections.Generic.List<(int Line, double From, double To, FormattedText Text)>();
        double block = SpeedLines * LabelSpacing;
        for (double start = LeftMargin + LabelSpacing / 2; start < width; start += block)
            for (int v = 1; v <= SpeedLines; v++)
            {
                double x = start + (v - 1) * LabelSpacing;
                if (x >= width)
                    break;
                var text = new FormattedText((v * 10).ToString(CultureInfo.InvariantCulture),
                                             CultureInfo.InvariantCulture, FlowDirection.LeftToRight, PrintFace, 15,
                                             PrintBrush);
                gaps.Add((v, x - 5, x + text.Width + 5, text));
            }

        for (int line = 0; line <= lines; line++)
        {
            double y = FieldBottom - line * Pitch;
            double from = 0;
            foreach (var gap in gaps)
                if (gap.Line == line)
                {
                    context.DrawLine(PrintPen, new Point(from, y), new Point(gap.From, y));
                    from = gap.To;
                }
            context.DrawLine(PrintPen, new Point(from, y), new Point(width, y));
        }

        foreach (var gap in gaps)
            context.DrawText(gap.Text, new Point(gap.From + 5, SpeedY(gap.Line * 10) - gap.Text.Height / 2));

        context.DrawLine(RedPen, new Point(0, RedTop), new Point(width, RedTop));
        context.DrawLine(RedPen, new Point(0, RedTop + RedGap), new Point(width, RedTop + RedGap));
    }

    /// <summary>
    /// The transport holes along both edges, one per 0.5 km, and the hour punched
    /// as a figure of dots into the top edge where the clock passes a full hour -
    /// midnight as 24.
    /// </summary>
    private void DrawEdges(DrawingContext context, HaslerTape tape, double height)
    {
        var figures = new System.Collections.Generic.List<(double From, double To)>();
        double nextHour = Math.Ceiling(ClockAt(tape.Samples[0]) / 3600) * 3600;
        foreach (var s in tape.Samples)
        {
            double clock = ClockAt(s);
            if (clock >= nextHour)
            {
                int hour = (int)(clock / 3600) % 24;
                figures.Add(PunchHour(context, hour == 0 ? 24 : hour, XOf(s)));
                nextHour = (Math.Floor(clock / 3600) + 1) * 3600;
            }
        }

        double nextHole = 0;
        foreach (var s in tape.Samples)
            if (s.DistanceM >= nextHole)
            {
                double x = XOf(s);
                if (!figures.Exists(f => x > f.From - 3 && x < f.To + 3))
                    Hole(context, x, TopEdge / 2);
                Hole(context, x, height - BottomEdge / 2);
                nextHole = (Math.Floor(s.DistanceM / PerforationStepM) + 1) * PerforationStepM;
            }
    }

    private static (double From, double To) PunchHour(DrawingContext context, int hour, double x)
    {
        const double dot = 3;
        string figures = hour.ToString(CultureInfo.InvariantCulture);
        double width = figures.Length * 6 * dot;
        double left = x - width / 2;
        double top = (TopEdge - 7 * dot) / 2 + dot / 2;
        for (int f = 0; f < figures.Length; f++)
        {
            string pattern = Digits[figures[f] - '0'];
            for (int i = 0; i < pattern.Length; i++)
                if (pattern[i] == '1')
                    context.DrawEllipse(HoleBrush, null,
                                        new Point(left + f * 6 * dot + i % 5 * dot, top + i / 5 * dot), 1.2, 1.2);
        }
        return (left, left + width);
    }

    private static void Hole(DrawingContext context, double x, double y)
    {
        context.DrawEllipse(HoleBrush, null, new Point(x, y), 2.4, 2.4);
        context.DrawEllipse(HoleBrush, null, new Point(x - 2.8, y), 0.9, 0.9);
        context.DrawEllipse(HoleBrush, null, new Point(x + 2.8, y), 0.9, 0.9);
    }

    private void DrawStylus(DrawingContext context, HaslerTape tape, Func<TapeSample, double> y)
    {
        var geometry = Trace(tape, y);
        context.DrawGeometry(null, StylusHalo, geometry);
        context.DrawGeometry(null, StylusPen, geometry);
    }

    /// <summary>The vigilance stylus rests under the upper red line and strokes up through it on every acknowledgement.</summary>
    private void DrawShp(DrawingContext context, HaslerTape tape)
    {
        double rest = RedTop + 6;
        double left = LeftMargin, right = TapeRight;
        context.DrawLine(StylusHalo, new Point(left, rest), new Point(right, rest));
        context.DrawLine(StylusPen, new Point(left, rest), new Point(right, rest));
        bool alarm = false;
        foreach (var s in tape.Samples)
        {
            if (alarm && !s.SecurityAlarm)
            {
                double x = XOf(s);
                context.DrawLine(StylusHalo, new Point(x, rest), new Point(x, RedTop - ShpReach));
                context.DrawLine(StylusPen, new Point(x, rest), new Point(x, RedTop - ShpReach));
            }
            alarm = s.SecurityAlarm;
        }
    }

    /// <summary>
    /// The paper, which repeats nowhere: the mottling is one bitmap stretched over the
    /// whole tape, and the grain, having no shapes to join up, is laid in patches from
    /// scattered places of its texture. A tiled ImageBrush loses tiles on screen, leaving
    /// holes in the paper; plain DrawImage calls do not, and the solid colour underneath
    /// covers anything the bitmaps do not.
    /// </summary>
    private static void DrawPaper(DrawingContext context, double width, double height)
    {
        context.DrawRectangle(PaperBrush, null, new Rect(0, 0, width, height));
        context.DrawImage(MottlingFor(width, height), new Rect(0, 0, width / MottlingCell, height / MottlingCell),
                          new Rect(0, 0, width, height));

        for (int row = 0; row * GrainPatch < height; row++)
            for (int column = 0; column * GrainPatch < width; column++)
            {
                uint place = Hash(column, row);
                // The last row and column are cut to the tape, not left hanging over its edge.
                var size = new Size(Math.Min(GrainPatch, width - column * GrainPatch),
                                    Math.Min(GrainPatch, height - row * GrainPatch));
                context.DrawImage(Grain, new Rect(new Point(place % GrainPatch, (place >> 16) % GrainPatch), size),
                                  new Rect(new Point(column * GrainPatch, row * GrainPatch), size));
            }
    }

    /// <summary>
    /// The light and dark clouds of the coating. Worked out from the place on the paper
    /// alone, so one bitmap serves every tape it is big enough for and only ever grows.
    /// </summary>
    private static Bitmap MottlingFor(double width, double height)
    {
        int columns = (int)Math.Ceiling(width / MottlingCell) + 1;
        int rows = (int)Math.Ceiling(height / MottlingCell) + 1;
        if (_mottling is not null && _mottling.PixelSize.Width >= columns && _mottling.PixelSize.Height >= rows)
            return _mottling;

        columns = Math.Max(columns + columns / 4, _mottling?.PixelSize.Width ?? 0);
        rows = Math.Max(rows, _mottling?.PixelSize.Height ?? 0);
        var pixels = new byte[columns * rows * 4];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                double shade = Noise(x, y, 5) * 18 + Noise(x, y, 23) * 12;
                int o = (y * columns + x) * 4;
                pixels[o] = Channel(165 + shade); // B
                pixels[o + 1] = Channel(148 + shade); // G
                pixels[o + 2] = Channel(122 + shade); // R
                pixels[o + 3] = 255;
            }

        return _mottling = ToBitmap(pixels, columns, rows, AlphaFormat.Opaque);
    }

    /// <summary>The fine grain of the coating and flecks of it already gone, laid over the mottling.</summary>
    private static Bitmap GrainTexture()
    {
        var random = new Random(2020);
        var pixels = new byte[GrainSize * GrainSize * 4];
        for (int o = 0; o < pixels.Length; o += 4)
        {
            double shade = (random.NextDouble() - 0.5) * 20;
            if (random.NextDouble() < 0.004)
                shade += 64; // a fleck of coating already gone

            // Premultiplied: white to lighten, black to darken.
            byte alpha = Channel(Math.Abs(shade));
            pixels[o] = pixels[o + 1] = pixels[o + 2] = shade > 0 ? alpha : (byte)0;
            pixels[o + 3] = alpha;
        }

        return ToBitmap(pixels, GrainSize, GrainSize, AlphaFormat.Premul);
    }

    /// <summary>Smooth noise over cells of the given size, -0.5 to 0.5; a place always gives the same value.</summary>
    private static double Noise(int x, int y, int cell)
    {
        int left = x / cell, top = y / cell;
        double across = Fade((double)(x % cell) / cell), down = Fade((double)(y % cell) / cell);
        return Lerp(Lerp(Corner(left, top), Corner(left + 1, top), across),
                    Lerp(Corner(left, top + 1), Corner(left + 1, top + 1), across), down);

        double Corner(int i, int j) => Hash(i, j + cell * 7919) / (double)uint.MaxValue - 0.5;
        static double Fade(double t) => t * t * (3 - 2 * t);
        static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }

    private static uint Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)x * 73856093u ^ (uint)y * 19349663u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            h *= 3266489917u;
            return h ^ h >> 16;
        }
    }

    private static byte Channel(double v) => (byte)Math.Clamp(v, 0, 255);

    private static Bitmap ToBitmap(byte[] pixels, int width, int height, AlphaFormat alpha)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, alpha);
        using (var buffer = bitmap.Lock())
            for (int y = 0; y < height; y++)
                Marshal.Copy(pixels, y * width * 4, buffer.Address + y * buffer.RowBytes, width * 4);

        return bitmap;
    }
}
