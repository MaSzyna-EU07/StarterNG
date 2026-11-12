using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Eu07.BlackBox.Views;

namespace Eu07.BlackBox.Viewer;

/// <summary>
/// BlackBoxViewer [file.blackbox] [--live] [--time seconds] [--follow] [--timetable | --summary] [--screenshot out.png]
/// --live follows a file the simulator still writes; --screenshot saves the window
/// at the moment given and quits, for documentation and checks.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ViewerApp>()
                  .UsePlatformDetect()
                  .WithInterFont();
}

internal sealed class ViewerApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var options = ViewerOptions.Parse(desktop.Args ?? Array.Empty<string>());
            var window = new BlackBoxWindow { WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen };
            window.Live = options.Live;
            desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                if (options.File is { } file)
                    await window.OpenAsync(Path.GetFullPath(file));
                window.ShowTimetable = options.Timetable;
                if (options.Summary)
                    window.SummaryShown = true;
                if (options.Screenshot is { } shot)
                {
                    await Screenshot.SaveAsync(window, shot, options.Time, options.Follow);
                    return;
                }
                if (options.Time is { } time)
                    window.SeekTo(time);
                window.FollowTrain = options.Follow;
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed record ViewerOptions(string? File, bool Live, double? Time, string? Screenshot, bool Follow, bool Timetable,
                                     bool Summary = false)
{
    public static ViewerOptions Parse(string[] args)
    {
        string? file = null, screenshot = null;
        double? time = null;
        bool live = false, follow = false, timetable = false, summary = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--live":
                    live = true;
                    break;
                case "--follow":
                    follow = true;
                    break;
                case "--timetable":
                    timetable = true;
                    break;
                case "--summary":
                    summary = true;
                    break;
                case "--time" when i + 1 < args.Length &&
                                   double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                                                   System.Globalization.CultureInfo.InvariantCulture, out var t):
                    time = t;
                    ++i;
                    break;
                case "--screenshot" when i + 1 < args.Length:
                    screenshot = args[++i];
                    break;
                default:
                    if (!args[i].StartsWith("--", StringComparison.Ordinal))
                        file ??= args[i];
                    break;
            }
        }
        return new ViewerOptions(file, live, time, screenshot, follow, timetable, summary);
    }
}
