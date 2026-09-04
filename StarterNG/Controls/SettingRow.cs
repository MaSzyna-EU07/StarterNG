using Avalonia;
using Avalonia.Controls;

namespace StarterNG.Controls;

/// <summary>
/// One line of the settings panel: label on the left, the control itself in the
/// middle, a value read-out on the right, and underneath the eu07.ini key this
/// row writes plus an optional description.
/// Replaces the hand-rolled StackPanel/DockPanel pattern that used to be repeated
/// for every setting.
/// </summary>
public class SettingRow : ContentControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Label));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));

    /// <summary>
    /// The eu07.ini key this row reads and writes, shown verbatim so a user can
    /// match what they see here against the file, the wiki or a forum post. Rows
    /// that are starter-only preferences carry their "starter.*" key just the
    /// same; rows that write no key at all leave this empty.
    /// </summary>
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

    /// <summary>
    /// Drives the template: an empty label lets the control span the label column,
    /// an empty description or value collapses that part of the row entirely.
    /// </summary>
    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":no-label", string.IsNullOrEmpty(Label));
        PseudoClasses.Set(":no-description", string.IsNullOrEmpty(Description));
        PseudoClasses.Set(":no-key", string.IsNullOrEmpty(ConfigKey));
        PseudoClasses.Set(":no-value", string.IsNullOrEmpty(ValueText));
        // A read-only row is a label and a reading with no widget between them. The
        // reading then takes the middle column too, so a long path ellipsises instead
        // of demanding its full width from an Auto column and getting cut off.
        PseudoClasses.Set(":no-content", Content is null);
    }
}
