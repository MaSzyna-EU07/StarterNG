using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using StarterNG.Classes;

namespace StarterNG;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        InitializeComponent();

        // Extend the client area over the title bar so the logo + navigation sit
        // on the top bar, while the OS caption buttons (min / max / close) stay in
        // the top-right corner. The default chrome hints already keep the system
        // caption buttons, so only the two supported hints are set here (this
        // Avalonia build no longer exposes ExtendClientAreaChromeHints).
        ExtendClientAreaToDecorationsHint = true;
        // Match the thin caption strip (row 0 of the top bar): the OS min/max/close
        // buttons fill this height at the top-right, sitting above the social row.
        ExtendClientAreaTitleBarHeightHint = 32;

        // Mirror the active language into the top-bar selector.
        LangCombo.SelectedIndex = App.Loc.CurrentLanguage == "Polski" ? 0 : 1;
    }

    // ── Navigation: show the page whose RadioButton was just checked ──────────
    private void Nav_OnCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true } rb)
            return;

        // Guard against the controls not being created yet during initial load.
        if (ScenariosView is null || DepotView is null || SettingsView is null)
            return;

        string page = rb.Tag as string ?? "scenarios";
        ScenariosView.IsVisible = page == "scenarios";
        DepotView.IsVisible = page == "depot";
        SettingsView.IsVisible = page == "settings";
    }

    private void LangCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: ComboBoxItem item }) return;
        if (!item.IsKeyboardFocusWithin) return; // ignore the programmatic initial set
        var lang = item.Content?.ToString();
        if (!string.IsNullOrEmpty(lang))
            App.ApplyLanguage(lang);
    }

    private void linkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn)
            return;

        if (btn.Tag is not string url || string.IsNullOrWhiteSpace(url))
            return;

        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }

    private void openLastLogClick(object? sender, RoutedEventArgs e)
    {
        string logPath = "log.txt";
        if (File.Exists(logPath))
        {
            Process.Start(new ProcessStartInfo(logPath)
            {
                UseShellExecute = true
            });
        }
    }

    // ── START: export the (possibly depot-modified) scenery and launch the sim.
    // Global on the main window, like the original Starter's bottom START button.
    private void StartButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var scenery = AppState.Instance.CurrentScenery;
        if (scenery is null)
            return;
        var trainset = AppState.Instance.CurrentTrainset;

        // write scenery/$<name>.scn with the replaced trainsets
        string dir = Path.GetDirectoryName(scenery.Path) ?? "scenery";
        string exportName = "$" + Path.GetFileName(scenery.Path);
        string exportPath = Path.Combine(dir, exportName);
        try
        {
            File.WriteAllText(exportPath, scenery.BuildExportContent(), Encoding.GetEncoding(1250));
        }
        catch
        {
            return; // couldn't write the scenery file
        }

        // the player's vehicle is the node name of the driven car in the consist
        string? vehicle = trainset?.Vehicles
            .FirstOrDefault(v => v.DriverType is eDriverType.Headdriver or eDriverType.Reardriver)?.Name
            ?? trainset?.Vehicles.FirstOrDefault()?.Name;

        // make sure the latest settings are on disk before the sim reads them
        Settings.Instance.CaptureAndSave();

        // launch the game: -s $<name>.scn -v <vehicle>
        try
        {
            Process.Start(new ProcessStartInfo(Settings.Instance.ExecutablePath)
            {
                Arguments = $"-s {exportName} -v {vehicle}",
                UseShellExecute = true
            });
        }
        catch
        {
            return; // executable not found / not configured yet
        }

        // honour the "close starter automatically" preference
        if (Settings.Instance.AutoCloseStarter)
            Close();
    }
}
