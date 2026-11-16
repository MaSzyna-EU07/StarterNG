using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using StarterNG.Application.Abstractions;
using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Infrastructure;

public static class LoadingScreen
{
    private static readonly string[] Extensions = { ".bmp", ".jpg", ".jpeg", ".png" };

    public static void Prepare(string? logoKey, string? sceneryName = null)
    {
        try
        {
            string? src = FindSource(new PhysicalFileSystem(), Directory.GetCurrentDirectory(), logoKey, sceneryName,
                                     Random.Shared);
            if (src is null) return;

            string destDir = Path.Combine(Directory.GetCurrentDirectory(), "textures");
            Directory.CreateDirectory(destDir);
            string dest = Path.Combine(destDir, "logo.bmp");

            if (src.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(src, dest, overwrite: true);
                return;
            }

            WriteBmp(src, dest);
        }
        catch (Exception ex)
        {
            Diagnostics.Log("Loading screen logo", ex);
        }
    }

    /// <summary>
    /// The picture of textures/logo under <paramref name="root"/> to show: the trainset's,
    /// else the scenery's, else one of logo*.* at random; its name and extension in any
    /// letter case. Null when there is none.
    /// </summary>
    public static string? FindSource(IFileSystem files, string root, string? logoKey, string? sceneryName,
                                     Random random)
    {
        var names = new CaseInsensitivePaths(files);
        if (names.Find(root, "textures/logo", directory: true) is not { } dir)
            return null;
        return Named(names, dir, logoKey) ?? Named(names, dir, sceneryName) ?? PickRandom(files, dir, random);
    }

    private static string? Named(CaseInsensitivePaths names, string dir, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        key = Path.GetFileNameWithoutExtension(key.Trim());
        if (key.Length == 0) return null;
        return Extensions.Select(ext => names.Find(dir, key + ext)).FirstOrDefault(path => path is not null);
    }

    private static string? PickRandom(IFileSystem files, string dir, Random random)
    {
        var logos = files.GetFiles(dir, "logo*.*")
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (logos.Length == 0) return null;
        return logos[random.Next(logos.Length)];
    }

    private static void WriteBmp(string sourcePath, string destPath)
    {
        using var src = new Bitmap(sourcePath);
        int w = src.PixelSize.Width;
        int h = src.PixelSize.Height;
        if (w <= 0 || h <= 0) return;

        var wb = new WriteableBitmap(src.PixelSize, src.Dpi, PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var fb = wb.Lock();
        src.CopyPixels(new PixelRect(0, 0, w, h), fb.Address, fb.RowBytes * h, fb.RowBytes);

        int srcStride = fb.RowBytes;
        var pixels = new byte[srcStride * h];
        Marshal.Copy(fb.Address, pixels, 0, pixels.Length);

        int rowStride = ((w * 3 + 3) / 4) * 4;
        int pixelBytes = rowStride * h;
        var file = new byte[54 + pixelBytes];

        file[0] = (byte)'B'; file[1] = (byte)'M';
        WriteInt32(file, 2, file.Length);
        WriteInt32(file, 10, 54);
        WriteInt32(file, 14, 40);
        WriteInt32(file, 18, w);
        WriteInt32(file, 22, h);
        WriteInt16(file, 26, 1);
        WriteInt16(file, 28, 24);
        WriteInt32(file, 34, pixelBytes);

        for (int y = 0; y < h; y++)
        {
            int destY = h - 1 - y;
            int destOff = 54 + destY * rowStride;
            int srcOff = y * srcStride;
            for (int x = 0; x < w; x++)
            {
                int si = srcOff + x * 4;
                int di = destOff + x * 3;
                file[di] = pixels[si];
                file[di + 1] = pixels[si + 1];
                file[di + 2] = pixels[si + 2];
            }
        }

        File.WriteAllBytes(destPath, file);
    }

    private static void WriteInt32(byte[] b, int o, int v)
    {
        b[o] = (byte)v; b[o + 1] = (byte)(v >> 8);
        b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24);
    }

    private static void WriteInt16(byte[] b, int o, int v)
    {
        b[o] = (byte)v; b[o + 1] = (byte)(v >> 8);
    }
}
