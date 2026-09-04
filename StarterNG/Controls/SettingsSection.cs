using Avalonia;
using Avalonia.Controls.Primitives;

namespace StarterNG.Controls;

public class SettingsSection : HeaderedContentControl
{
    public static readonly StyledProperty<object?> ToggleProperty =
        AvaloniaProperty.Register<SettingsSection, object?>(nameof(Toggle));

    public object? Toggle
    {
        get => GetValue(ToggleProperty);
        set => SetValue(ToggleProperty, value);
    }
}
