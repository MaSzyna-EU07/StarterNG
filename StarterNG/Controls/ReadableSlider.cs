using System;
using System.Globalization;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace StarterNG.Controls;

/// <summary>
/// A slider that tells a screen reader the value it shows beside it. Avalonia's own
/// gives only the bare number, which NVDA reads as a share of the slider's length:
/// -5 °C was read as 45, a fog of 2 km as wherever the knob stood on its scale.
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
