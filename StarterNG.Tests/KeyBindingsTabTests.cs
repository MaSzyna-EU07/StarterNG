using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using StarterNG.Application;
using StarterNG.Classes;
using StarterNG.Controls;
using StarterNG.Tests.Fakes;
using StarterNG.Views;

namespace StarterNG.Tests;

/// <summary>The key bindings as someone at the keyboard binds a key: the list of commands, and the keyboard drawn beside it walked with the arrows.</summary>
[Collection(AvaloniaControls.Name)]
public class KeyBindingsTabTests
{
    static KeyBindingsTabTests()
    {
        if (AppServices.CurrentOrNull is null)
        {
            var installation = new TestInstallation(new InMemoryFileSystem());
            AppServices.Initialize(installation.Paths, installation.Files, installation.Clock, installation.Environment);
        }
    }

    private sealed record Shown(Window Window, KeyBindingsTab Tab, Grid Host, Announcer Announcer) : IDisposable
    {
        public List<Border> Caps() =>
            Host.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("KeyCap")).ToList();

        public void Dispose() => Window.Close();
    }

    private static Shown Open()
    {
        KeyboardConfig.Instance.Parse("hornhighactivate a\nhornlowactivate none\n");
        var host = new Grid();
        var search = new TextBox();
        var tab = new KeyBindingsTab(host, search);
        var announcer = new Announcer { Child = new DockPanel { Children = { search, host } } };
        var window = Controls.Show(announcer, 1600, 1000, themed: true);
        window.AddHandler(InputElement.KeyDownEvent, tab.OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        tab.Build();
        window.UpdateLayout();
        Controls.Settle();
        return new Shown(window, tab, host, announcer);
    }

    [Fact]
    public void The_arrows_walk_the_keys_as_they_lie()
    {
        using var shown = Open();
        var caps = shown.Caps();
        Assert.True(caps.Count > 50);
        Assert.All(caps, c => Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(c))));

        caps[0].Focus();
        Assert.True(Controls.Press(caps[0], Key.Right));
        Assert.True(caps[1].IsFocused);
        Assert.True(Controls.Press(caps[1], Key.Left));
        Assert.True(caps[0].IsFocused);
        Assert.True(Controls.Press(caps[0], Key.Left));
        Assert.True(caps[0].IsFocused);

        Assert.True(Controls.Press(caps[0], Key.End));
        Assert.True(caps[^1].IsFocused);
        Assert.True(Controls.Press(caps[^1], Key.Right));
        Assert.True(caps[^1].IsFocused);
        Assert.True(Controls.Press(caps[^1], Key.Home));
        Assert.True(caps[0].IsFocused);

        Assert.True(Controls.Press(caps[0], Key.Down));
        var below = caps.Single(c => c.IsFocused);
        Assert.NotSame(caps[0], below);
        Assert.True(Controls.Press(below, Key.Up));
        var above = caps.Single(c => c.IsFocused);
        Assert.NotSame(below, above);
        Assert.True(Controls.Press(above, Key.Up));
        Assert.True(above.IsFocused);

        Assert.False(Controls.Press(caps[0], Key.F24));
    }

    [Fact]
    public void A_binding_takes_the_next_key_pressed_and_esc_gives_up_waiting()
    {
        using var shown = Open();
        Assert.True(shown.Tab.ShownCount >= 2);
        var combos = shown.Host.GetVisualDescendants().OfType<Button>()
                          .Where(b => b.Classes.Contains("Flat") && b.MinWidth == 140).ToList();
        Assert.NotEmpty(combos);

        combos[0].RaiseEvent(Controls.Click());
        Assert.True(shown.Tab.IsCapturing);
        Assert.True(Controls.Press(combos[0], Key.Escape));
        Assert.False(shown.Tab.IsCapturing);

        combos[0].RaiseEvent(Controls.Click());
        Assert.False(Controls.Press(combos[0], Key.LeftShift));
        Assert.True(shown.Tab.IsCapturing);
        Assert.True(Controls.Press(combos[0], Key.J, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.False(shown.Tab.IsCapturing);
        Assert.True(KeyboardConfig.Instance.Dirty);
    }
}
