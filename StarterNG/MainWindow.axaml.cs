using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StarterNG.Classes;
using StarterNG.Domain;
using StarterNG.Infrastructure;
using StarterNG.Services;
using StarterNG.Views;
using SettingsModel = StarterNG.Classes.Settings;

namespace StarterNG;

public partial class MainWindow : Window
{
    // Height of the caption strip the OS buttons live in.
    private const int TitleBarHeight = 32;

    private DateTime _openedUtc = DateTime.UtcNow;

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
        ExtendClientAreaTitleBarHeightHint = TitleBarHeight;

        // Windows draws the min / max / close buttons over the top of the extended
        // client area, on top of the toolbar row. Push the bar down by the caption
        // height so they get a strip of their own.
        if (OperatingSystem.IsWindows())
            TopBar.Padding = new Thickness(0, TitleBarHeight, 0, 0);

        PopulateLangCombo();
        App.Loc.LanguageChanged += OnLanguageChanged;

        // Keep Start enabled/caption in sync with selection (Pascal actStartUpdate).
        AppState.Instance.Changed += UpdateStartButton;
        Opened += (_, _) =>
        {
            _openedUtc = DateTime.UtcNow;
            UpdateStartButton();
        };
        Activated += (_, _) => CheckExternalSettings();
    }

    // Lets the user drag the window by the top caption strip.
    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
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

    // One entry per file in lang/. The entries are language names, which are not
    // translated, so this runs once - never from OnLanguageChanged, where it
    // would clear the combo from inside its own SelectionChanged and throw.
    private void PopulateLangCombo()
    {
        LangCombo.Items.Clear();
        foreach (var lang in LocalizationService.AvailableLanguages())
            LangCombo.Items.Add(new ComboBoxItem { Content = lang.Name, Tag = lang.Code });
        if (LangCombo.ItemCount == 0)
            LangCombo.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });

        SelectActiveLanguage();
    }

    // Points the combo at the loaded language; a no-op when it already shows it,
    // which is the case for a pick made here.
    private void SelectActiveLanguage()
    {
        var item = LangCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(it => string.Equals(it.Tag as string, App.Loc.CurrentLangCode,
                                                StringComparison.OrdinalIgnoreCase))
            ?? LangCombo.Items.OfType<ComboBoxItem>().FirstOrDefault();

        if (item != null && !ReferenceEquals(LangCombo.SelectedItem, item))
            LangCombo.SelectedItem = item;
    }

    private void LangCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if ((LangCombo.SelectedItem as ComboBoxItem)?.Tag is not string code)
            return;

        // Filling the combo in selects the language that is already loaded, which
        // lands here as well - comparing against the loaded one is what tells the
        // two apart, and it keeps the handler idempotent.
        if (string.Equals(code, App.Loc.CurrentLangCode, StringComparison.OrdinalIgnoreCase))
            return;

        App.ApplyLanguage(code);
    }

    // Captions built in code (train stats, panel headers, combo items) are read
    // from App.Loc once, so they have to be built again. Runs for a pick made
    // here or in the settings tab, hence hanging off Loc rather off the combo.
    private void OnLanguageChanged()
    {
        SelectActiveLanguage();
        ScenariosView?.RebuildAfterLanguageChange();
        DepotView?.RebuildAfterLanguageChange();
        UpdateStartButton();
    }

    private void HelpButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        var wiki = new MenuItem { Header = App.Loc["HelpWiki"] };
        wiki.Click += (_, _) => OpenPathOrUrl("https://wiki.eu07.pl/");
        menu.Items.Add(wiki);

        string readme = string.Equals(App.Loc.CurrentLangCode, "pl", StringComparison.OrdinalIgnoreCase)
            ? "readme.html" : "en-readme.html";
        if (File.Exists(readme) || File.Exists(Path.Combine(Directory.GetCurrentDirectory(), readme)))
        {
            var mi = new MenuItem { Header = App.Loc["HelpReadme"] };
            mi.Click += (_, _) => OpenPathOrUrl(readme);
            menu.Items.Add(mi);
        }

        string regs = Path.Combine(Directory.GetCurrentDirectory(), "przepisy_kolejowe");
        if (Directory.Exists(regs))
        {
            menu.Items.Add(new Separator());
            foreach (string pdf in Directory.GetFiles(regs, "*.pdf").OrderBy(Path.GetFileName))
            {
                string file = pdf;
                var mi = new MenuItem { Header = Path.GetFileName(file) };
                mi.Click += (_, _) => OpenPathOrUrl(file);
                menu.Items.Add(mi);
            }
        }

        menu.Open(HelpButton);
    }

    private void linkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn)
            return;

        if (btn.Tag is not string url || string.IsNullOrWhiteSpace(url))
            return;

        OpenPathOrUrl(url);
    }

    private static void OpenPathOrUrl(string pathOrUrl)
    {
        try
        {
            string target = pathOrUrl;
            if (!pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                target = Path.GetFullPath(pathOrUrl);
                if (!File.Exists(target) && !Directory.Exists(target))
                    return;
            }
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch { }
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

    // Pascal actStartUpdate: Start needs a scenery and a trainset. A consist with
    // nobody aboard still starts, after the confirmation in StartButton_OnClick.
    private void UpdateStartButton()
    {
        if (startButton is null) return;

        var scenery = AppState.Instance.CurrentScenery;
        var trainset = AppState.Instance.CurrentTrainset;

        if (scenery is null)
        {
            startButton.Content = App.Loc["StartNoScenery"];
            startButton.IsEnabled = false;
            return;
        }
        if (trainset is null)
        {
            startButton.Content = App.Loc["StartNoTrain"];
            startButton.IsEnabled = false;
            return;
        }
        // No driver and no passenger: still startable, but say so on the button
        // and ask before launching.
        bool staffed = HasStartableVehicle(trainset);
        startButton.Content = staffed ? App.Loc["Start"] : App.Loc["StartNoStaff"];
        ToolTip.SetTip(startButton, staffed ? null : App.Loc["StartNoStaffConfirm"]);
        startButton.IsEnabled = true;
    }

    private static bool HasStartableVehicle(Trainset trainset)
    {
        static bool CanStart(Dynamic v) =>
            v.DriverType is eDriverType.Headdriver or eDriverType.Reardriver or eDriverType.Passenger;

        string? selected = AppState.Instance.StartingVehicleName;
        if (!string.IsNullOrEmpty(selected))
        {
            var pick = trainset.Vehicles.FirstOrDefault(v =>
                string.Equals(v.Name, selected, StringComparison.OrdinalIgnoreCase));
            if (pick != null)
                return CanStart(pick);
        }
        return trainset.Vehicles.Any(CanStart);
    }

    // ── START: export the (possibly depot-modified) scenery and launch the sim.
    // Global on the main window, like the original Starter's bottom START button.
    private async void StartButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var scenery = AppState.Instance.CurrentScenery;
        if (scenery is null)
            return;
        var trainset = AppState.Instance.CurrentTrainset;
        if (trainset is null)
            return;

        // Launching a consist with no driver and no passenger is legal (a scenery
        // can be watched from the outside), so confirm instead of blocking it.
        if (!HasStartableVehicle(trainset) &&
            !await MessageBox.Show(this, App.Loc["StartNoStaffConfirm"],
                                   App.Loc["StartNoStaffTitle"], MessageBoxButtons.YesNo))
            return;

        if (trainset.Vehicles.Count > 0)
        {
            trainset.Velocity = SettingsModel.Instance.BatteryDefault switch
            {
                BatteryDefault.AlwaysOff => 0f,
                BatteryDefault.AlwaysOn  => MathF.Abs(trainset.OriginalVelocity) < 0.01f ? 0.1f : trainset.OriginalVelocity,
                _                        => trainset.OriginalVelocity
            };
        }

        // Pascal UniqueVehicleName / UniqueVehiclesName before writing $scn.
        string? vehicle = TrainsetDisplay.UniquifyForLaunch(
            trainset, scenery, AppState.Instance.StartingVehicleName);
        if (string.IsNullOrEmpty(vehicle))
            vehicle = ResolveStartVehicle(trainset);
        AppState.Instance.StartingVehicleName = vehicle;

        LoadingScreen.Prepare(trainset.Logo, Path.GetFileNameWithoutExtension(scenery.Path));

        // Always inject the Weather-tab values (incl. start-time override), like the
        // Pascal starter's ChangeConfig on every launch — not only after an edit.
        scenery.WeatherDirty = true;

        // write scenery/$<name>.scn with the replaced trainsets
        string dir = Path.GetDirectoryName(scenery.Path) ?? "scenery";
        string exportName = "$" + Path.GetFileName(scenery.Path);
        string exportPath = Path.Combine(dir, exportName);
        try
        {
            File.WriteAllText(exportPath, scenery.BuildExportContent(SettingsModel.Instance.IgnoreIrrelevantTrains), Encoding.GetEncoding(1250));
        }
        catch
        {
            return; // couldn't write the scenery file
        }

        // make sure the latest settings are on disk before the sim reads them
        SettingsModel.Instance.CaptureAndSave();

        // launch the game: -s $<name>.scn -v <vehicle>. Keep the handle so we can
        // watch for the simulator exiting. UseShellExecute = false runs the binary
        // directly with its arguments, which works the same on Windows and Linux
        // (the executable is resolved to eu07.exe / eu07 via ResolveExecutable).
        Process? sim;
        try
        {
            string exe = Path.GetFullPath(SettingsModel.Instance.ResolveExecutable());
            sim = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"-s {exportName} -v {vehicle}",
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Directory.GetCurrentDirectory(),
                UseShellExecute = false
            });
        }
        catch
        {
            return; // executable not found / not configured yet
        }

        // honour the "close starter automatically" preference
        if (SettingsModel.Instance.AutoCloseStarter)
        {
            Close();
            return;
        }

        // Otherwise keep the starter running but hidden while the sim is in the
        // foreground, and bring it back once the simulator process exits.
        Hide();

        // reduce ram usage while the sim runs
        GC.Collect();
        GC.WaitForPendingFinalizers();

        WatchSimulator(sim);
    }

    // Pascal: RunInfo.Vehicle := Train.Vehicles[SelVehicle].Name after actStartUpdate
    // requires headdriver/reardriver/passenger on that car.
    private static string? ResolveStartVehicle(Trainset? trainset)
    {
        if (trainset is null || trainset.Vehicles.Count == 0)
            return null;

        static bool CanStart(Dynamic v) =>
            v.DriverType is eDriverType.Headdriver or eDriverType.Reardriver or eDriverType.Passenger;

        string? selected = AppState.Instance.StartingVehicleName;
        if (!string.IsNullOrEmpty(selected))
        {
            var pick = trainset.Vehicles.FirstOrDefault(v =>
                string.Equals(v.Name, selected, StringComparison.OrdinalIgnoreCase));
            if (pick != null && CanStart(pick))
                return pick.Name;
        }

        return trainset.Vehicles.FirstOrDefault(CanStart)?.Name
               ?? trainset.Vehicles.FirstOrDefault()?.Name;
    }

    // Waits (off the UI thread) for the simulator to exit, then restores the
    // starter window. Uses the launch handle when available, otherwise locates the
    // process by name, and never leaves the UI hidden if it cannot be tracked.
    private void WatchSimulator(Process? sim)
    {
        if (sim is null)
        {
            string name = Path.GetFileNameWithoutExtension(SettingsModel.Instance.ResolveExecutable());
            sim = Process.GetProcessesByName(name).FirstOrDefault();
        }

        if (sim is null)
        {
            // no handle to wait on - restore immediately rather than hide forever
            Dispatcher.UIThread.Post(RestoreFromSimulator);
            return;
        }

        var watcher = new Thread(() =>
        {
            try { sim.WaitForExit(); }
            catch { /* already exited / access denied */ }
            Dispatcher.UIThread.Post(RestoreFromSimulator);
        })
        {
            IsBackground = true,
            Name = "SimulatorWatcher"
        };
        watcher.Start();
    }

    // Pascal CheckSettingsFile on AppActivate (skip the first few seconds).
    private async void CheckExternalSettings()
    {
        if ((DateTime.UtcNow - _openedUtc).TotalSeconds < 3)
            return;
        if (!SettingsModel.Instance.IsConfigNewerOnDisk())
            return;

        var result = await MessageBox.Show(
            this,
            App.Loc["SettingsFileChanged"],
            App.Loc["SettingsFileChangedTitle"],
            MessageBoxButtons.YesNo);
        if (result)
        {
            SettingsModel.Instance.Load();
            SettingsView?.ReloadFromSettings();
        }
        else
            SettingsModel.Instance.TouchConfigAge();
    }

    // Brings the starter back to the foreground after the simulator closes.
    private void RestoreFromSimulator()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
