using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using StarterNG.Application;
using StarterNG.Domain.Settings;
using StarterNG.Infrastructure;

namespace StarterNG.Views;

// What the graphics and effects categories add to the screen: the renderer and the
// anti-aliasing, and the import and export of eu07.ini.
public partial class Settings
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly FilePickerFileType[] IniTypes =
    {
        new("eu07.ini") { Patterns = new[] { "*.ini" } },
        new("*") { Patterns = new[] { "*" } }
    };

    private void ShowGraphics(SimulatorSettings s)
    {
        ShowRenderer(s);
        SelectTag(AntiAliasingCb, AntiAliasingTag(s.AntiAliasing));
        _fullscreenAsRead = s.Fullscreen;
        SelectTag(DisplayModeCb, DisplayModeOf(s.Fullscreen, s.FullscreenWindowed));
        ShowMonitor(s.FullscreenMonitor);
        UpdateDisplayRows();
    }

    private void CaptureGraphics(SimulatorSettings s)
    {
        if (_fileRendererItem is not null && ReferenceEquals(RenderEngineCb.SelectedItem, _fileRendererItem))
        {
            s.RenderEngine = 0;
            s.RendererWord = _fileRendererItem.Tag as string;
        }
        else
        {
            s.RenderEngine = Math.Max(0, RenderEngineCb.SelectedIndex);
            s.RendererWord = null;
        }
        if (!ShowsFileValue(AntiAliasingCb))
            s.AntiAliasing = AntiAliasingFromTag(SelectedTag(AntiAliasingCb));
        s.FullscreenMonitor = SelectedTag(MonitorCb) ?? string.Empty;

        string mode = SelectedTag(DisplayModeCb) ?? FullscreenMode;
        s.FullscreenWindowed = mode == BorderlessMode;
        // Borderless overrides fullscreen in the simulator, so the file's own word for it stays.
        s.Fullscreen = mode == BorderlessMode ? _fullscreenAsRead : mode == FullscreenMode;
    }

    /// <summary>The renderer the file names in a word the list does not offer, listed under that word.</summary>
    private ComboBoxItem? _fileRendererItem;

    private void ShowRenderer(SimulatorSettings s)
    {
        if (_fileRendererItem is not null)
        {
            if (s.RendererWord is not null && Equals(_fileRendererItem.Tag, s.RendererWord))
            {
                RenderEngineCb.SelectedItem = _fileRendererItem;
                return;
            }
            RenderEngineCb.Items.Remove(_fileRendererItem);
            _fileRendererItem = null;
        }

        if (s.RendererWord is { } word)
        {
            _fileRendererItem = new ComboBoxItem { Tag = word, Content = word };
            RenderEngineCb.Items.Add(_fileRendererItem);
            RenderEngineCb.SelectedItem = _fileRendererItem;
        }
        else
            RenderEngineCb.SelectedIndex = s.RenderEngine;
    }

    // ── How the simulator shows: full screen, borderless or in a window ──────

    private const string FullscreenMode = "fullscreen";
    private const string BorderlessMode = "borderless";
    private const string WindowMode = "window";

    /// <summary>fullscreen as the file has it, kept where borderless makes it of no account.</summary>
    private bool _fullscreenAsRead;

    /// <summary>
    /// The mode the two keys make (application.cpp): fullscreenwindowed takes the monitor's
    /// own size and turns full screen on whatever fullscreen says.
    /// </summary>
    private static string DisplayModeOf(bool fullscreen, bool windowed) =>
        windowed ? BorderlessMode : fullscreen ? FullscreenMode : WindowMode;

    /// <summary>A borderless screen takes the monitor's size; a window can't be put on a monitor.</summary>
    private void UpdateDisplayRows()
    {
        string mode = SelectedTag(DisplayModeCb) ?? FullscreenMode;
        Dim(ResolutionRow, mode == BorderlessMode);
        Dim(MonitorRow, mode == WindowMode);
    }

    /// <summary>The whole row greyed out, its label and description too, as a disabled check box is.</summary>
    private static void Dim(Control row, bool off)
    {
        row.IsEnabled = !off;
        row.Opacity = off ? 0.45 : 1;
    }

    private ComboBoxItem? _primaryMonitorItem;

    private IReadOnlyList<DisplayMonitor>? _monitors;

    /// <summary>The monitors attached, read once.</summary>
    private IReadOnlyList<DisplayMonitor> MonitorList => _monitors ??= Monitors.List(AppServices.Current.Environment);

    /// <summary>
    /// The monitor the simulator will show on (application.cpp): a full screen, borderless or
    /// not, goes to the one chosen, or the primary one when that is not connected; a window
    /// opens where the system puts it, as a rule the primary one. Null where no monitor could be read.
    /// </summary>
    private DisplayMonitor? SimulatorMonitor()
    {
        string? chosen = SelectedTag(DisplayModeCb) == WindowMode ? null : SelectedTag(MonitorCb);
        DisplayMonitor? primary = null;
        foreach (var m in MonitorList)
        {
            if (!string.IsNullOrEmpty(chosen) && m.Id == chosen)
                return m;
            if (m.Primary)
                primary ??= m;
        }
        return primary;
    }

    /// <summary>
    /// The monitors found, listed once, with the primary one first; a monitor the file names
    /// that is not connected now stays on the list under its own name.
    /// </summary>
    private void ShowMonitor(string id)
    {
        if (_primaryMonitorItem is null)
        {
            _primaryMonitorItem = new ComboBoxItem { Tag = string.Empty, Content = App.Loc["MonitorPrimary"] };
            App.Loc.LanguageChanged += () => _primaryMonitorItem.Content = App.Loc["MonitorPrimary"];
            MonitorCb.Items.Add(_primaryMonitorItem);
            var monitors = MonitorList;
            for (int i = 0; i < monitors.Count; i++)
            {
                var m = monitors[i];
                MonitorCb.Items.Add(new ComboBoxItem
                {
                    Tag = m.Id,
                    Content = $"{i + 1}. {m.Name} – {m.Width}×{m.Height}"
                });
            }
        }

        if (IndexOfTag(MonitorCb, id) < 0)
            MonitorCb.Items.Add(new ComboBoxItem { Tag = id, Content = id });
        SelectTag(MonitorCb, id);
    }

    private async void ImportIniButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var picked = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = App.Loc["ImportIniTitle"],
            AllowMultiple = false,
            FileTypeFilter = IniTypes
        });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } path)
            return;

        if (!AppServices.Current.SettingsStore.ImportFrom(path))
        {
            await MessageBox.Show(owner, string.Format(App.Loc["ImportIniFailed"], path), App.Loc["ImportIni"],
                                  MessageBoxButtons.Ok);
            return;
        }

        ApplyToUi();
        // What came from the other file is a choice to save, not a value the screen could not show.
        _shownUnlikeSettings = new Dictionary<string, ShownValue>();
        MarkDirty();
        SaveStatus.Text = string.Format(App.Loc["ImportIniDone"], Path.GetFileName(path));
    }

    private async void ExportIniButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = App.Loc["ExportIniTitle"],
            SuggestedFileName = "eu07.ini",
            DefaultExtension = "ini",
            FileTypeChoices = IniTypes,
            ShowOverwritePrompt = true
        });
        if (file?.TryGetLocalPath() is not { } path)
            return;

        // The edits on screen go into the export as they would into a save, but the
        // user's own eu07.ini is not written, nor the settings held: the edits stay the page's, not saved.
        if (!AppServices.Current.SettingsStore.ExportTo(path, Trial()))
        {
            await MessageBox.Show(owner, string.Format(App.Loc["ExportIniFailed"], path), App.Loc["ExportIni"],
                                  MessageBoxButtons.Ok);
            return;
        }

        SaveStatus.Text = string.Format(App.Loc["ExportIniDone"], Path.GetFileName(path));
    }

    private static void SetNote(TextBlock note, string text)
    {
        note.Text = text;
        note.IsVisible = text.Length > 0;
    }

    private static string? SelectedTag(ComboBox cb) => (cb.SelectedItem as ComboBoxItem)?.Tag as string;

    private static int TagInt(ComboBox cb, int fallback) =>
        int.TryParse(SelectedTag(cb), NumberStyles.Integer, Invariant, out int n) ? n : fallback;

    private static void SelectTag(ComboBox cb, string? tag)
    {
        int index = IndexOfTag(cb, tag);
        if (index >= 0)
            cb.SelectedIndex = index;
    }

    /// <summary>Selects the item of the tag, adding one for a value from the file the list does not offer.</summary>
    private static void SelectOrAddTag(ComboBox cb, string tag, string label)
    {
        int index = IndexOfTag(cb, tag);
        if (index < 0)
        {
            cb.Items.Add(new ComboBoxItem { Tag = tag, Content = label });
            index = cb.Items.Count - 1;
        }
        cb.SelectedIndex = index;
    }

    private static string AntiAliasingTag(AntiAliasingMode mode) => mode switch
    {
        AntiAliasingMode.Msaa2 => "msaax2",
        AntiAliasingMode.Msaa4 => "msaax4",
        AntiAliasingMode.Msaa8 => "msaax8",
        _ => "off"
    };

    private static AntiAliasingMode AntiAliasingFromTag(string? tag) => tag switch
    {
        "msaax2" => AntiAliasingMode.Msaa2,
        "msaax4" => AntiAliasingMode.Msaa4,
        "msaax8" => AntiAliasingMode.Msaa8,
        _ => AntiAliasingMode.Off
    };
}
