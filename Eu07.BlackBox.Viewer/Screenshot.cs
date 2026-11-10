using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Eu07.BlackBox.Views;

namespace Eu07.BlackBox.Viewer;

/// <summary>The window as it shows a moment, saved to a PNG; then the viewer closes.</summary>
internal static class Screenshot
{
    public static async Task SaveAsync(BlackBoxWindow window, string path, double? time, bool follow)
    {
        if (time is { } t)
            window.SeekTo(t);
        window.FollowTrain = follow;
        // A few frames for the layout and the first render.
        await Task.Delay(1000);
        if (window.Content is Control content)
        {
            var size = new PixelSize((int)content.Bounds.Width, (int)content.Bounds.Height);
            using var bitmap = new RenderTargetBitmap(size);
            bitmap.Render(content);
            bitmap.Save(path, new PngBitmapEncoderOptions());
        }
        window.Close();
    }
}
