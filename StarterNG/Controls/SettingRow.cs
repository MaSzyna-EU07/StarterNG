using Avalonia;
using Avalonia.Controls;

namespace StarterNG.Controls;

public class SettingRow : ContentControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Label));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));

    public static readonly StyledProperty<string?> ConfigKeyProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(ConfigKey));

    public static readonly StyledProperty<string?> ValueTextProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(ValueText));

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string? ConfigKey
    {
        get => GetValue(ConfigKeyProperty);
        set => SetValue(ConfigKeyProperty, value);
    }

    public string? ValueText
    {
        get => GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    public SettingRow()
    {
        UpdatePseudoClasses();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LabelProperty ||
            change.Property == DescriptionProperty ||
            change.Property == ConfigKeyProperty ||
            change.Property == ValueTextProperty ||
            change.Property == ContentProperty)
            UpdatePseudoClasses();
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":no-label", string.IsNullOrEmpty(Label));
        PseudoClasses.Set(":no-description", string.IsNullOrEmpty(Description));
        PseudoClasses.Set(":no-key", string.IsNullOrEmpty(ConfigKey));
        PseudoClasses.Set(":no-value", string.IsNullOrEmpty(ValueText));
        PseudoClasses.Set(":no-content", Content is null);
    }
}
