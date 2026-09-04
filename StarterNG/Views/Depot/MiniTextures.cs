using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using StarterNG.Classes;

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

    public Bitmap? Get(string? name, int height, bool strict = false)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        if (strict && !VehicleDatabase.HasMini(name))
            return null;

        string key = $"{(strict ? "!" : "")}{name}@{height}";
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        Bitmap? bmp = null;
        string? path = VehicleDatabase.MiniPath(name);
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
