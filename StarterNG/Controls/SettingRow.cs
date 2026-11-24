using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

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
    /// A setting for those who know what it does: the standard mode leaves it out, as
    /// one a player can break the game with or has no need of.
    /// </summary>
    public static readonly StyledProperty<bool> AdvancedProperty =
        AvaloniaProperty.Register<SettingRow, bool>(nameof(Advanced));

    public bool Advanced
    {
        get => GetValue(AdvancedProperty);
        set => SetValue(AdvancedProperty, value);
    }

    /// <summary>The value eu07.ini gives the setting that the starter cannot read, said under it; null when there is none.</summary>
    public static readonly StyledProperty<string?> UnknownValueProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(UnknownValue));

    public string? UnknownValue
    {
        get => GetValue(UnknownValueProperty);
        set => SetValue(UnknownValueProperty, value);
    }

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
            change.Property == UnknownValueProperty ||
            change.Property == ContentProperty)
            UpdatePseudoClasses();

        if (change.Property == LabelProperty || change.Property == ContentProperty)
            ScreenReaderNames.NameInputs(Content, Label);

        if (change.Property == ValueTextProperty || change.Property == ContentProperty)
            ScreenReaderNames.TellValue(Content, ValueText);

        if (change.Property == DescriptionProperty || change.Property == UnknownValueProperty ||
            change.Property == ContentProperty)
            ScreenReaderNames.DescribeInputs(Content, Description, UnknownValue);
    }

    // Again once in the window: a panel given as content may be filled after it was set.
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ScreenReaderNames.NameInputs(Content, Label);
        ScreenReaderNames.TellValue(Content, ValueText);
        ScreenReaderNames.DescribeInputs(Content, Description, UnknownValue);
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":no-label", string.IsNullOrEmpty(Label));
        PseudoClasses.Set(":no-description", string.IsNullOrEmpty(Description));
        PseudoClasses.Set(":no-key", string.IsNullOrEmpty(ConfigKey));
        PseudoClasses.Set(":no-value", string.IsNullOrEmpty(ValueText));
        PseudoClasses.Set(":no-content", Content is null);
        PseudoClasses.Set(":unknown", !string.IsNullOrEmpty(UnknownValue));
    }
}
