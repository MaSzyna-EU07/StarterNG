using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using StarterNG.Infrastructure;

namespace StarterNG.Views;

/// <summary>
/// The starter's event log and the bulk depot tools. The options it used to carry
/// live in the Advanced category of the settings now, where they are found.
/// </summary>
public sealed class LogAndToolsWindow : Window
{
    private readonly TextBox _log = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = MonoFont.Family,
        FontSize = 12
    };

    public event Action<string>? ToolRequested;

    public LogAndToolsWindow()
    {
        Title = App.Loc["LogAndTools"];
        Width = 620;
        Height = 470;
        MinWidth = 480;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = App.Loc["AdvLogPage"], Content = BuildLogPage() });
        tabs.Items.Add(new TabItem { Header = App.Loc["Tools"], Content = BuildToolsPage() });

        var close = new Button
        {
            Content = App.Loc["Close"],
            MinWidth = 100,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        close.Classes.Add("Accent");
        close.Click += (_, _) => Close();

        var root = new DockPanel { Margin = new Avalonia.Thickness(10) };
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(tabs);
        Content = root;
    }

    private Control BuildLogPage()
    {
        _log.Text = Diagnostics.Text;

        var clear = new Button
        {
            Content = App.Loc["AdvClearLog"],
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        clear.Classes.Add("Flat");
        clear.Click += (_, _) => { Diagnostics.Clear(); _log.Text = ""; };

        ScrollViewer.SetHorizontalScrollBarVisibility(_log, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_log, ScrollBarVisibility.Auto);

        var panel = new DockPanel { Margin = new Avalonia.Thickness(8) };
        DockPanel.SetDock(clear, Dock.Bottom);
        panel.Children.Add(clear);
        panel.Children.Add(_log);
        return panel;
    }

    private Control BuildToolsPage()
    {
        var sp = new StackPanel { Margin = new Avalonia.Thickness(10), Spacing = 8 };

        Button Tool(string key, string tag)
        {
            var b = new Button
            {
                Content = App.Loc[key],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            b.Classes.Add("Flat");
            b.Click += (_, _) => ToolRequested?.Invoke(tag);
            return b;
        }

        sp.Children.Add(Tool("AddAllCategory", "category"));
        sp.Children.Add(Tool("AddUniqueMmd", "mmd"));
        sp.Children.Add(Tool("RemoveAllTrains", "removeall"));
        return sp;
    }
}
