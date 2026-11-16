using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StarterNG.Controls;

/// <summary>
/// What every window opened over another does for someone at the keyboard, as a dialog
/// of Windows does: it opens with focus on its default button or else its first
/// control, where it opened with nothing focused and a screen reader said only its
/// title; Esc closes it; Tab with nothing focused starts from the first control.
/// </summary>
public static class DialogKeyboard
{
    /// <summary>False for a window that stays open beside the main one, such as the tapes: Esc there is not a way out.</summary>
    public static readonly Avalonia.AttachedProperty<bool> EscapeClosesProperty =
        Avalonia.AvaloniaProperty.RegisterAttached<Window, bool>("EscapeCloses", typeof(DialogKeyboard), defaultValue: true);

    public static bool GetEscapeCloses(Window window) => window.GetValue(EscapeClosesProperty);

    public static void SetEscapeCloses(Window window, bool value) => window.SetValue(EscapeClosesProperty, value);

    public static void Install() =>
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => OnOpened(window));

    private static void OnOpened(Window window)
    {
        // The main window and the splash have no owner, and their own ways.
        if (window.Owner is null)
            return;

        window.AddHandler(InputElement.KeyDownEvent, (_, e) => OnTab(window, e), RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => OnEscape(window, e));
        Dispatcher.UIThread.Post(() => FocusFirst(window), DispatcherPriority.Loaded);
    }

    // Bubbling and only when nothing took it: a list closing its drop-down, a cancel
    // button or a window of its own that handles the key keep it.
    private static void OnEscape(Window window, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || !GetEscapeCloses(window))
            return;

        window.Close();
        e.Handled = true;
    }

    private static void OnTab(Window window, KeyEventArgs e)
    {
        if (window.FocusManager is not { } focus)
            return;

        var next = KeyboardFocus.TabFromNowhere(e.Key, e.KeyModifiers, focus.GetFocusedElement(), window,
                                                focus.FindFirstFocusableElement, focus.FindLastFocusableElement);
        if (next is not null && next.Focus(NavigationMethod.Tab))
            e.Handled = true;
    }

    /// <summary>Focus on the default button, or the first control when there is none; left alone when the window placed it itself.</summary>
    public static void FocusFirst(Window window)
    {
        if (window.FocusManager is not { } focus)
            return;

        var focused = focus.GetFocusedElement();
        if (focused is not null && !ReferenceEquals(focused, window))
            return;

        IInputElement? target = window.GetVisualDescendants().OfType<Button>()
                                    .FirstOrDefault(b => b.IsDefault && b.IsEffectivelyVisible && b.IsEffectivelyEnabled)
                                ?? focus.FindFirstFocusableElement();
        target?.Focus(NavigationMethod.Tab);
    }
}
