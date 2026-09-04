using Avalonia;
using Avalonia.Controls.Primitives;

namespace StarterNG.Controls;

/// <summary>
/// A group of <see cref="SettingRow"/>s under a flat title with an accent rule.
/// <see cref="Toggle"/> holds an optional master switch shown on the right of the
/// title line; the section content gates itself off it.
/// </summary>
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
