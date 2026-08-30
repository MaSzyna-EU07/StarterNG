using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using StarterNG.Views;

namespace StarterNG.Infrastructure;

public static class Diagnostics
{
    private static readonly object Gate = new();
    private static readonly List<string> Lines = new();

    private static StreamWriter? _writer;
    private static bool _writerFailed;

    public static string LogPath =>
        Path.Combine(Directory.GetCurrentDirectory(), "starter", "bledy.txt");

    public static string Text
    {
        get { lock (Gate) return string.Join(Environment.NewLine, Lines); }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Lines.Clear();
            CloseWriter();
            try
            {
                if (File.Exists(LogPath))
                    File.Delete(LogPath);
            }
            catch
            {
            }
        }
    }

    public static void Log(string message)
    {
        string line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}  {message}";
        lock (Gate)
        {
            Lines.Add(line);
            Append(line);
        }
    }

    public static void Log(IEnumerable<string> messages)
    {
        foreach (string message in messages)
            Log(message);
    }

    public static void Log(string context, Exception ex) =>
        Log($"{context}: {ex.GetType().Name}: {ex.Message}");

    public static void Flush()
    {
        lock (Gate)
        {
            if (Lines.Count == 0)
                return;

            try
            {
                _writer?.Flush();
            }
            catch (Exception ex)
            {
                _writerFailed = true;
                Lines.Add($"log flush failed: {ex.GetType().Name}: {ex.Message}");
            }

            if (!_writerFailed && _writer is not null)
                return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.WriteAllLines(LogPath, Lines);
            }
            catch
            {
            }
        }
    }

    private static void Append(string line)
    {
        if (_writerFailed)
            return;

        try
        {
            if (_writer is null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                _writer = new StreamWriter(LogPath, append: false) { AutoFlush = true };
            }
            _writer.WriteLine(line);
        }
        catch
        {
            _writerFailed = true;
            CloseWriter();
        }
    }

    private static void CloseWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
        }
        _writer = null;
    }

    private static Window? Owner =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;

    public static Task ReportAsync(string message, string? title = null)
    {
        Log(message.Replace(Environment.NewLine, " | "));

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

    public static List<string> CheckInstallation()
    {
        var faults = new List<string>();
        string root = Directory.GetCurrentDirectory();

        foreach (string folder in new[] { "dynamic", "sounds", "models", "scenery", "textures" })
            if (!Directory.Exists(Path.Combine(root, folder)))
                faults.Add(string.Format(App.Loc["FaultNoDir"], "/" + folder));

        if (!File.Exists(Path.Combine(root, "data", "load_weights.txt")))
            faults.Add(App.Loc["FaultNoWeights"]);

        if (Classes.GameData.Instance.Sceneries.Count == 0)
            faults.Add(App.Loc["FaultNoScenery"]);

        if (Classes.GameData.Instance.Vehicles.Textures.Count == 0)
            faults.Add(App.Loc["FaultNoVehicles"]);

        if (Classes.Physics.IndexedCount == 0)
            faults.Add(App.Loc["FaultNoPhysics"]);

        if (Classes.Settings.Instance.ResolveExecutable(out var problem) is var _ &&
            problem == Classes.ExeProblem.NotFound)
            faults.Add(App.Loc["FaultNoExe"]);

        return faults;
    }
}
