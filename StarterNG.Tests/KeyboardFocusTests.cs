using Avalonia.Controls;
using Avalonia.Input;
using StarterNG.Controls;

namespace StarterNG.Tests;

public class KeyboardFocusTests
{
    private readonly Button _window = new();
    private readonly Button _first = new();
    private readonly Button _last = new();

    private IInputElement? Tab(Key key, KeyModifiers modifiers, IInputElement? focused) =>
        KeyboardFocus.TabFromNowhere(key, modifiers, focused, _window, () => _first, () => _last);

    [Fact]
    public void Tab_with_nothing_focused_starts_from_the_first_control()
    {
        Assert.Same(_first, Tab(Key.Tab, KeyModifiers.None, null));
    }

    [Fact]
    public void Shift_tab_with_only_the_window_focused_starts_from_the_last_control()
    {
        Assert.Same(_last, Tab(Key.Tab, KeyModifiers.Shift, _window));
    }

    [Fact]
    public void Tab_from_a_focused_control_is_left_to_the_usual_order()
    {
        Assert.Null(Tab(Key.Tab, KeyModifiers.None, new TextBox()));
    }

    [Fact]
    public void Other_keys_are_left_alone()
    {
        Assert.Null(Tab(Key.Enter, KeyModifiers.None, null));
    }
}
