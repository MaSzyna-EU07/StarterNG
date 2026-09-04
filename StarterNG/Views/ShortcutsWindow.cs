using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using StarterNG.Infrastructure;

namespace StarterNG.Views;

/// <summary>
/// The cheat sheet, printed straight from <see cref="Shortcuts.All"/> so it can never
/// list a gesture the window does not actually dispatch.
/// </summary>
public static class ShortcutsWindow
{
    public static Window Create()
    {
        var list = new StackPanel { Spacing = 2, Margin = new Thickness(20, 12, 20, 20) };

        foreach (var scope in new[] { ShortcutScope.Global, ShortcutScope.Depot, ShortcutScope.Consist })
        {
            list.Children.Add(new TextBlock
            {
                Text = Shortcuts.ScopeLabel(scope),
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 12, 0, 4)
            });

            foreach (var s in Shortcuts.InScope(scope))
                list.Children.Add(Row(s.Display, App.Loc[s.LabelKey]));
        }

        list.Children.Add(new TextBlock
        {
            Text = App.Loc["ShortcutsTabHint"],
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 16, 0, 0)
        });

        var win = new Window
        {
            Title = App.Loc["ShortcutsTitle"],
            Width = 460,
            Height = 560,
            MinWidth = 380,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = list
            }
        };

        win.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.F1)
            {
                win.Close();
                e.Handled = true;
            }
        };
        return win;
    }

    private static Control Row(string gesture, string label)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,12,*") };

        var keys = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#2A3037")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3A424A")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = gesture,
                FontFamily = new FontFamily("Consolas, Menlo, monospace"),
                FontSize = 12
            }
        };

        var text = new TextBlock
        {
            Text = label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(text, 2);

        grid.Children.Add(keys);
        grid.Children.Add(text);
        grid.Margin = new Thickness(0, 2);
        return grid;
    }
}
