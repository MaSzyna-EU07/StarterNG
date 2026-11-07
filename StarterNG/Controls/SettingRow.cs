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

    /// <summary>
    /// Why the simulator to be started would pass this setting over; the row dims
    /// and says so. The input stays usable - the file may serve another build.
    /// </summary>
    public static readonly StyledProperty<string?> IdleNoteProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(IdleNote));

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

    public string? IdleNote
    {
        get => GetValue(IdleNoteProperty);
        set => SetValue(IdleNoteProperty, value);
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
            change.Property == IdleNoteProperty ||
            change.Property == ContentProperty)
            UpdatePseudoClasses();

        if (change.Property == IdleNoteProperty)
            ToolTip.SetTip(this, string.IsNullOrEmpty(IdleNote) ? null : IdleNote);
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":no-label", string.IsNullOrEmpty(Label));
        PseudoClasses.Set(":no-description", string.IsNullOrEmpty(Description));
        PseudoClasses.Set(":no-key", string.IsNullOrEmpty(ConfigKey));
        PseudoClasses.Set(":no-value", string.IsNullOrEmpty(ValueText));
        PseudoClasses.Set(":no-content", Content is null);
        PseudoClasses.Set(":idle", !string.IsNullOrEmpty(IdleNote));
    }
}
