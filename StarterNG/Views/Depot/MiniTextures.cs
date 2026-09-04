using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using StarterNG.Classes;
using StarterNG.Application;

namespace StarterNG.Views;

public sealed class MiniTextures
{
    private readonly Dictionary<string, Bitmap?> _cache = new();

    /// <summary>
    /// Draws a mini without smoothing. These files are about 80x30 and get shown up
    /// to 68 px tall, so bilinear just smears the upscale; nearest keeps the edges.
    /// (Has to be set in code - Avalonia 12 exposes RenderOptions as a struct with
    /// Get/Set helpers, not as a styleable attached property.)
    /// </summary>
    public static Image Sharp(Image image)
    {
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
        return image;
    }

    /// <summary>Body of the drawn stand-in; the black wagon of the old starter.</summary>
    private static readonly IBrush MissingBody = new SolidColorBrush(Color.Parse("#0B0C0D"));

    private static readonly IBrush MissingEdge = new SolidColorBrush(Color.Parse("#3A424A"));

    private static readonly IBrush MissingMark = new SolidColorBrush(Color.Parse("#E6E8EA"));

    /// <summary>Real minis are about 80x30, and the stand-in keeps that shape.</summary>
    private const double MiniAspect = 80.0 / 30.0;

    /// <summary>
    /// The stand-in for a vehicle with no thumbnail: a dark wagon marked with
    /// question marks, the way the old starter drew textures/mini/other.bmp.
    /// </summary>
    /// <remarks>
    /// Reached only when the installation has no other.bmp either - a vehicle
    /// naming a thumbnail we do not have already falls back to that file. Drawn
    /// rather than loaded so the consist keeps its length whatever is missing;
    /// a vehicle that is simply absent from the strip reads as a shorter train.
    /// </remarks>
    public static Control MissingVisual(string? label, int height)
    {
        double width = height * MiniAspect;
        double bodyTop = height * 0.23;
        double bodyHeight = height * 0.47;
        double wheelRadius = height * 0.13;

        var canvas = new Canvas { Width = width, Height = height };

        var body = new Border
        {
            Width = width - 4,
            Height = bodyHeight,
            Background = MissingBody,
            BorderBrush = MissingEdge,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "? ?",
                FontSize = Math.Max(9, height * 0.34),
                FontWeight = FontWeight.SemiBold,
                Foreground = MissingMark,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Canvas.SetLeft(body, 2);
        Canvas.SetTop(body, bodyTop);
        canvas.Children.Add(body);

        foreach (double centre in new[] { width * 0.22, width * 0.78 })
        {
            var wheel = new Ellipse
            {
                Width = wheelRadius * 2,
                Height = wheelRadius * 2,
                Fill = MissingBody
            };
            Canvas.SetLeft(wheel, centre - wheelRadius);
            Canvas.SetTop(wheel, bodyTop + bodyHeight - wheelRadius);
            canvas.Children.Add(wheel);
        }

        if (!string.IsNullOrWhiteSpace(label))
            ToolTip.SetTip(canvas, label);

        return canvas;
    }

    public Bitmap? Get(string? name, int height, bool strict = false)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        if (strict && !AppServices.Current.MiniTextures.Has(name))
            return null;

        string key = $"{(strict ? "!" : "")}{name}@{height}";
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        Bitmap? bmp = null;
        string? path = AppServices.Current.MiniTextures.PathFor(name);
        if (path != null)
        {
            try
            {
                // Decode at native size and let the renderer scale. The minis are
                // about 80x30, so DecodeToHeight would bake a 2x upscale into the
                // cache - blurry, and five times the memory. Only shrink here.
                using var fs = File.OpenRead(path);
                bmp = new Bitmap(fs);
                if (bmp.PixelSize.Height > height)
                {
                    bmp.Dispose();
                    fs.Position = 0;
                    bmp = Bitmap.DecodeToHeight(fs, height);
                }
            }
            catch (System.Exception ex)
            {
                Infrastructure.Diagnostics.Log($"mini/{name}", ex);
                bmp = null;
            }
        }
        _cache[key] = bmp;
        return bmp;
    }
}
