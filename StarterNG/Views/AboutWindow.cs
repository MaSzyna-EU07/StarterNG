using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using StarterNG.Infrastructure;

namespace StarterNG.Views;

public static class AboutWindow
{
    private const string RepositoryUrl = "https://github.com/maj00r/StarterNG";

    private static readonly string[] ChangelogNames =
    {
        "changelog.txt", "starter/changelog.txt", "CHANGELOG.md"
    };

    public static Window Create()
    {
        var win = new Window
        {
            Title = App.Loc["AboutTitle"],
            Width = 640,
            Height = 460,
            MinWidth = 480,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var header = new StackPanel { Spacing = 2, Margin = new Avalonia.Thickness(20, 18, 20, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "Starter MaSzyna",
            FontSize = 22,
            FontWeight = FontWeight.Bold
        });
        header.Children.Add(new TextBlock
        {
            Text = $"{App.Loc["Version"]} {AppInfo.Version}",
            Opacity = 0.8
        });
        header.Children.Add(new TextBlock
        {
            Text = App.Loc["AboutForSimulator"],
            Opacity = 0.8
        });

        var repo = new Button
        {
            Content = RepositoryUrl,
            Tag = RepositoryUrl,
            Padding = new Avalonia.Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Avalonia.Thickness(0, 6, 0, 10),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        repo.Classes.Add("Basic");
        repo.Click += (_, _) => OpenUrl(RepositoryUrl);
        header.Children.Add(repo);

        var changelog = new TextBox
        {
            Text = ReadChangelog(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = MonoFont.Family,
            FontSize = 12,
            Margin = new Avalonia.Thickness(20, 0, 20, 12)
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(changelog, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(changelog, ScrollBarVisibility.Auto);

        var close = new Button
        {
            Content = App.Loc["Close"],
            MinWidth = 90,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        close.Classes.Add("Accent");
        close.Click += (_, _) => win.Close();

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(20, 0, 20, 16),
            Children = { close }
        };

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);

        var logLabel = new TextBlock
        {
            Text = App.Loc["AboutChangelog"],
            Margin = new Avalonia.Thickness(20, 0, 20, 4),
            Opacity = 0.8
        };
        DockPanel.SetDock(logLabel, Dock.Top);
        root.Children.Add(logLabel);
        root.Children.Add(changelog);

        win.Content = root;
        return win;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Infrastructure.Diagnostics.Log($"Open {url}", ex);
        }
    }

    private static string ReadChangelog()
    {
        foreach (string name in ChangelogNames)
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), name);
            if (!File.Exists(path)) continue;
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"Changelog {path}", ex);
            }
        }
        return App.Loc["AboutNoChangelog"];
    }
}
