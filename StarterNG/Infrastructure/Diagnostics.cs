using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using StarterNG.Application;
using StarterNG.Views;

namespace StarterNG.Infrastructure;

public static class Diagnostics
{
    private static Application.Abstractions.IDiagnosticsLog Log_ => AppServices.Current.Log;

    public static string LogPath => AppServices.Current.Paths.DiagnosticsLog;

    public static string Text => Log_.Text;

    public static void Clear() => Log_.Clear();

    public static void Log(string message) => Log_.Log(message);

    public static void Log(IEnumerable<string> messages) => Log_.Log(messages);

    public static void Log(string context, Exception ex) => Log_.Log(context, ex);

    public static void Flush() => Log_.Flush();

    private static Window? Owner =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;

    public static Task ReportAsync(string message, string? title = null)
    {
        Log_.Log(message.Replace(Environment.NewLine, " | "));

        var owner = Owner;
        if (owner is null)
            return Task.CompletedTask;

        return MessageBox.Show(owner, message, title ?? App.Loc["FaultTitle"], MessageBoxButtons.Ok);
    }

    public static Task ReportAsync(string context, Exception ex, string? title = null) =>
        ReportAsync($"{context}{Environment.NewLine}{Environment.NewLine}{App.Loc["FaultDetail"]} {ex.Message}", title);

    public static void ReportOnUiThread(string message, string? title = null)
    {
        if (Dispatcher.UIThread.CheckAccess())
            _ = ReportAsync(message, title);
        else
            Dispatcher.UIThread.Post(() => _ = ReportAsync(message, title));
    }

    public static List<string> CheckInstallation() =>
        new InstallationCheck(
                AppServices.Current.Paths,
                AppServices.Current.Files,
                AppServices.Current.Localization,
                AppServices.Current.Physics,
                AppServices.Current.Library,
                AppServices.Current.SettingsStore)
            .Run();
}
