using Avalonia.Media;
using StarterNG.Application;

namespace StarterNG.Views;

public static class VehicleCardStyle
{
    public static readonly IBrush Border = new SolidColorBrush(Color.Parse("#33888888"));
    public static readonly IBrush BorderSelected = new SolidColorBrush(Color.Parse("#AA41C400"));
    public static readonly IBrush BackgroundSelected = new SolidColorBrush(Color.Parse("#2241C400"));

    /// <summary>Lit state of the flip button on a reversed vehicle.</summary>
    public static readonly IBrush Flipped = new SolidColorBrush(Color.Parse("#41C400"));

    public static readonly IBrush Placeholder = new SolidColorBrush(Color.Parse("#22808080"));

    public static int ThumbHeight =>
        AppServices.Current.Settings.LargeThumbnails ? 68 : 34;
}
