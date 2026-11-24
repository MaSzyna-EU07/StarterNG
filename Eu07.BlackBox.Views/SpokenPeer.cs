using System;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace Eu07.BlackBox.Views;

/// <summary>
/// What a screen reader is told of a control that draws itself: a name, the moment or
/// the row it stands at as its value - read again whenever it changes under the focus,
/// as a slider's is - and a description of what it shows or how to work it.
/// </summary>
internal sealed class SpokenPeer : ControlAutomationPeer, IValueProvider
{
    private readonly AutomationControlType _type;
    private readonly Func<string?> _value;
    private readonly Func<string?> _help;

    public SpokenPeer(Control owner, AutomationControlType type, Func<string?> value, Func<string?> help) : base(owner)
    {
        _type = type;
        _value = value;
        _help = help;
    }

    /// <summary>Tells the reader the value changed, so it reads the new one.</summary>
    public void Told(string? before, string? now)
    {
        if (before != now)
            RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, before, now);
    }

    protected override AutomationControlType GetAutomationControlTypeCore() => _type;

    protected override string? GetHelpTextCore() => base.GetHelpTextCore() is { Length: > 0 } own ? own : _help();

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    string? IValueProvider.Value => _value();

    bool IValueProvider.IsReadOnly => true;

    void IValueProvider.SetValue(string? value) => throw new InvalidOperationException("The value is read only.");
}
