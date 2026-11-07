using Avalonia;
using Avalonia.Controls;

namespace StarterNG.Controls;

/// <summary>
/// Where the settings layout wants the row of one of the SimulatorKeys, so it
/// sits among the rows it belongs with; the row is put in when the page is built.
/// </summary>
public sealed class KeySlot : Decorator
{
    public static readonly StyledProperty<string?> KeyProperty =
        AvaloniaProperty.Register<KeySlot, string?>(nameof(Key));

    /// <summary>The eu07.ini key whose row goes here.</summary>
    public string? Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }
}
