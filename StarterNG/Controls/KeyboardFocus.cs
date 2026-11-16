using System;
using Avalonia.Input;

namespace StarterNG.Controls;

public static class KeyboardFocus
{
    /// <summary>
    /// Focus that fell out with a control taken away or switched off leaves the window
    /// itself focused, and Tab then went nowhere until something was clicked - for
    /// someone without a mouse, nowhere at all. Tab starts again from the first
    /// control, Shift+Tab from the last. Null when focus is somewhere and Tab moves it
    /// as usual.
    /// </summary>
    public static IInputElement? TabFromNowhere(Key key, KeyModifiers modifiers, IInputElement? focused,
                                                IInputElement window, Func<IInputElement?> first,
                                                Func<IInputElement?> last)
    {
        if (key != Key.Tab || focused is not null && !ReferenceEquals(focused, window))
            return null;

        return modifiers.HasFlag(KeyModifiers.Shift) ? last() : first();
    }
}
