using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace StarterNG.Tests;

/// <summary>Keys, focus, layout and drawing for the tests of controls, with no one at the keyboard.</summary>
internal static class Controls
{
    /// <summary>Presses a key on the control as the keyboard would; true when the control took it.</summary>
    public static bool Press(Control control, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = control };
        control.RaiseEvent(e);
        return e.Handled;
    }

    /// <summary>
    /// The control in a window of its own, shown, laid out and focused if it can be;
    /// themed, its parts are built from the templates the application uses.
    /// </summary>
    public static Window Show(Control control, double width = 640, double height = 720, bool themed = false)
    {
        HeadlessDrawing.Ensure();
        if (themed)
            Theme();
        var window = new Window { Width = width, Height = height, Content = control };
        window.Show();
        window.UpdateLayout();
        Settle();
        if (control.Focusable)
            control.Focus();
        return window;
    }

    /// <summary>Lays the control out alone at the size given.</summary>
    public static void Lay(Control control, double width = 640, double height = 720)
    {
        HeadlessDrawing.Ensure();
        control.Measure(new Size(width, height));
        control.Arrange(new Rect(0, 0, width, height));
    }

    /// <summary>Has the control draw itself once.</summary>
    public static void Draw(Control control)
    {
        HeadlessDrawing.Ensure();
        var group = new DrawingGroup();
        using var context = group.Open();
        control.Render(context);
    }

    private static bool _themed;

    // Where the application keeps it: given to a window alone, a slider or a number box was left without its parts.
    private static void Theme()
    {
        if (_themed)
            return;
        Avalonia.Application.Current!.Styles.Add(new FluentTheme());
        _themed = true;
    }

    /// <summary>Runs what was left for the dispatcher to do later, such as naming a row once it is laid out.</summary>
    public static void Settle() => Dispatcher.UIThread.RunJobs();

    public static AutomationPeer Peer(Control control) => ControlAutomationPeer.CreatePeerForElement(control);

    public static RoutedEventArgs Click() => new(Button.ClickEvent);
}
