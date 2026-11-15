using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace StarterNG.Controls;

/// <summary>
/// A slider that tells a screen reader the value it shows beside it. Avalonia's own
/// gives only the bare number, which NVDA reads as a share of the slider's length:
/// -5 °C was read as 45, a fog of 2 km as wherever the knob stood on its scale.
/// The track under the mouse says the same, where its bare buttons and knob said nothing.
/// </summary>
public class ReadableSlider : Slider
{
    public static readonly StyledProperty<string?> ValueTextProperty =
        AvaloniaProperty.Register<ReadableSlider, string?>(nameof(ValueText));

    /// <summary>The value as written for the reader, such as "2,4 km"; the number when unset.</summary>
    public string? ValueText
    {
        get => GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Slider);

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    /// <summary>What a screen reader is told the slider holds.</summary>
    public string SpokenValue => SpokenText(ValueText, Value);

    public static string SpokenText(string? valueText, double value) =>
        string.IsNullOrWhiteSpace(valueText)
            ? value.ToString("0.##", CultureInfo.CurrentCulture)
            : valueText.Trim();

    /// <summary>What the mouse finds over the track: the slider's name and its value.</summary>
    public string TrackText =>
        AutomationProperties.GetName(this) is { } name && !string.IsNullOrWhiteSpace(name)
            ? $"{name.Trim()}: {SpokenValue}"
            : SpokenValue;

    private Track? _track;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _track = e.NameScope.Find<Track>("PART_Track");
        NameTrack();
    }

    // The knob gets its own template only when first laid out.
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        NameTrack();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == ValueTextProperty
            || change.Property == AutomationProperties.NameProperty)
            NameTrack();
    }

    private void NameTrack()
    {
        if (_track is null)
            return;

        string text = TrackText;
        foreach (var part in _track.GetVisualDescendants().OfType<Control>().Prepend(_track))
            AutomationProperties.SetName(part, text);
    }

    private sealed class Peer : RangeBaseAutomationPeer, IValueProvider
    {
        private readonly ReadableSlider _slider;

        // A moved knob is told by the range peer already; the reader then asks for this
        // text, set by then. Telling the text's change too would have it spoken twice.
        public Peer(ReadableSlider slider) : base(slider) => _slider = slider;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

        protected override string GetClassNameCore() => "Slider";

        string? IValueProvider.Value => _slider.SpokenValue;

        bool IValueProvider.IsReadOnly => !_slider.IsEnabled;

        void IValueProvider.SetValue(string? value)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double number))
                throw new ArgumentException($"Not a number: {value}", nameof(value));
            _slider.Value = number;
        }
    }
}
