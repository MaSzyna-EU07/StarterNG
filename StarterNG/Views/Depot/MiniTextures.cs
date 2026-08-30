using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;

using StarterNG.Classes;

namespace StarterNG.Views;

public sealed class MiniTextures
{
    private readonly Dictionary<string, Bitmap?> _cache = new();

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
                using var fs = File.OpenRead(path);
                bmp = Bitmap.DecodeToHeight(fs, height);
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
