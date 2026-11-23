using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace StarterNG.Controls;

public class SettingsSection : HeaderedContentControl
{
    /// <summary>Every row of the section is for the advanced mode only.</summary>
    public static readonly StyledProperty<bool> AdvancedProperty =
        AvaloniaProperty.Register<SettingsSection, bool>(nameof(Advanced));

    public bool Advanced
    {
        get => GetValue(AdvancedProperty);
        set => SetValue(AdvancedProperty, value);
    }

    public static readonly StyledProperty<object?> ToggleProperty =
        AvaloniaProperty.Register<SettingsSection, object?>(nameof(Toggle));

    public static readonly StyledProperty<string?> ToggleKeyProperty =
        AvaloniaProperty.Register<SettingsSection, string?>(nameof(ToggleKey));

    public static readonly StyledProperty<string?> ResetLabelProperty =
        AvaloniaProperty.Register<SettingsSection, string?>(nameof(ResetLabel));

    public static readonly StyledProperty<object?> LevelProperty =
        AvaloniaProperty.Register<SettingsSection, object?>(nameof(Level));

    private Button? _reset;

    /// <summary>The header's choice of a level of detail for this section alone; none where it has no such thing.</summary>
    public object? Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public object? Toggle
    {
        get => GetValue(ToggleProperty);
        set => SetValue(ToggleProperty, value);
    }

    /// <summary>The eu07.ini key the header's switch sets.</summary>
    public string? ToggleKey
    {
        get => GetValue(ToggleKeyProperty);
        set => SetValue(ToggleKeyProperty, value);
    }

    /// <summary>The words on the header's button that puts the section back as the simulator has it; none, no button.</summary>
    public string? ResetLabel
    {
        get => GetValue(ResetLabelProperty);
        set => SetValue(ResetLabelProperty, value);
    }

    /// <summary>Raised when the header's reset button is pressed.</summary>
    public event Action<SettingsSection>? ResetRequested;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_reset is not null)
            _reset.Click -= OnResetClick;
        _reset = e.NameScope.Find<Button>("PART_Reset");
        if (_reset is not null)
            _reset.Click += OnResetClick;
        NameReset();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HeaderProperty || change.Property == ResetLabelProperty)
            NameReset();
        if (change.Property == HeaderProperty || change.Property == LevelProperty)
            NameLevel();
    }

    /// <summary>
    /// The header's list of levels was named by the word it shows with nothing chosen,
    /// "Własne"; it is named as the level of this section.
    /// </summary>
    private void NameLevel()
    {
        if (Level is Control level)
            Avalonia.Automation.AutomationProperties.SetName(level, ScreenReaderNames.PickerText(
                AppLanguage.Text("SectionLevel"), ScreenReaderNames.TextOf(Header)));
    }

    /// <summary>
    /// Every section has a button with the same words; a reader is told which section
    /// it puts back, as a sighted one sees from where it stands.
    /// </summary>
    private void NameReset()
    {
        if (_reset is null)
            return;

        string? name = ScreenReaderNames.PickerText(ResetLabel, ScreenReaderNames.TextOf(Header));
        if (string.IsNullOrEmpty(name))
            _reset.ClearValue(Avalonia.Automation.AutomationProperties.NameProperty);
        else
            Avalonia.Automation.AutomationProperties.SetName(_reset, name);
    }

    private void OnResetClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ResetRequested?.Invoke(this);
}
