using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StarterNG.Classes;
using StarterNG.Domain;
using StarterNG.Infrastructure;
using StarterNG.Services;
using StarterNG.Views;
using SettingsModel = StarterNG.Classes.Settings;
using StarterNG.Application;
using StarterNG.Domain.Sceneries;

namespace StarterNG;

public partial class MainWindow : Window
{

    private const int TitleBarHeight = 32;

    private DateTime _openedUtc = DateTime.UtcNow;

    public MainWindow()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        InitializeComponent();

        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = TitleBarHeight;

        if (SettingsModel.Instance.WindowMaximized)
            WindowState = WindowState.Maximized;

        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty && WindowState != WindowState.Minimized)
                SettingsModel.Instance.WindowMaximized = WindowState == WindowState.Maximized;
        };

        if (OperatingSystem.IsWindows())
            TopBar.Padding = new Thickness(0, TitleBarHeight, 0, 0);

        PopulateLangCombo();
        App.Loc.LanguageChanged += OnLanguageChanged;

        startButton.AddHandler(PointerReleasedEvent, StartButton_OnRightClick,
                               RoutingStrategies.Tunnel);

        SettingsView.ThumbnailSizeChanged += () =>
        {
            DepotView.RefreshConsistView();
            ScenariosView.RefreshConsistView();
        };

        ScenariosView.RandomizeTexturesRequested += async scenery =>
        {
            await DepotView.RandomizeSceneryTrainsetsAsync(scenery);
            ScenariosView.RefreshConsistView();
        };

        NavSettings.AddHandler(PointerReleasedEvent, NavSettings_OnRightClick,
                               RoutingStrategies.Tunnel);

        ApplyInitialNav();

        // Tunnelling: the shortcuts are checked before any view sees the key, which is
        // why OnShortcutKey has to be careful about text entry and key capture.
        AddHandler(KeyDownEvent, OnShortcutKey, RoutingStrategies.Tunnel);

        AppState.Instance.Changed += UpdateStartButton;
        Opened += (_, _) =>
        {

            _openedUtc = DateTime.UtcNow;
            UpdateStartButton();
        };
        Activated += (_, _) => CheckExternalSettings();
    }

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void NavSettings_OnRightClick(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right)
            return;

        var win = new Views.AdvancedSettingsWindow();
        win.ToolRequested += tool =>
        {
            switch (tool)
            {
                case "category": DepotView.ToolAddAllCategory(); break;
                case "mmd": DepotView.ToolAddUniqueMmd(); break;
                case "removeall": DepotView.ToolRemoveAllTrains(); break;
            }
        };
        win.ShowDialog(this);
        e.Handled = true;
    }

    private void OnShortcutKey(object? sender, KeyEventArgs e)
    {
        // A cab-control binding in Settings is waiting for this very keypress.
        if (SettingsView?.IsCapturingKey == true)
            return;

        var mods = e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt);
        if (Shortcuts.Match(e.Key, mods) is not { } id)
            return;

        // Bare keys belong to whatever is being typed into; Ctrl/Alt combinations do not
        // collide with text entry, so those stay live everywhere.
        if (mods == KeyModifiers.None && IsTextEntryFocused())
            return;

        switch (id)
        {
            case Shortcuts.Start:
                _ = LaunchAsync(saveSettings: true, freeFly: false);
                break;

            case Shortcuts.PageScenarios: NavScenarios.IsChecked = true; break;
            case Shortcuts.PageDepot: NavDepot.IsChecked = true; break;
            case Shortcuts.PageSettings: NavSettings.IsChecked = true; break;

            case Shortcuts.ShowHelp:
                Views.ShortcutsWindow.Create().ShowDialog(this);
                break;

            default:
                if (!DepotView.IsVisible)
                    return;
                switch (id)
                {
                    case Shortcuts.FocusSearch: DepotView.KeyFocusSearch(); break;
                    case Shortcuts.AddVehicle: DepotView.KeyAddVehicle(); break;
                    case Shortcuts.RemoveVehicle: DepotView.KeyRemoveVehicle(); break;
                    case Shortcuts.ClearConsist: DepotView.KeyClearConsist(); break;
                    case Shortcuts.SetNumber: DepotView.KeySetWagonNumber(); break;
                    default: return;
                }
                break;
        }

        e.Handled = true;
    }

    /// <summary>
    /// Anything that swallows plain typing: text boxes, but also the editable inner box
    /// of a NumericUpDown or AutoCompleteBox.
    /// </summary>
    private bool IsTextEntryFocused() =>
        FocusManager?.GetFocusedElement() is Visual focused
        && (focused is TextBox || focused.FindAncestorOfType<TextBox>() != null);

    private void Nav_OnCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true } rb)
            return;

        if (ScenariosView is null || DepotView is null || SettingsView is null)
            return;

        ShowPage(rb.Tag as string);
    }

    private void ShowPage(string? page)
    {
        page ??= "scenarios";
        ScenariosView.IsVisible = page == "scenarios";
        DepotView.IsVisible = page == "depot";
        SettingsView.IsVisible = page == "settings";
    }

    private void ApplyInitialNav()
    {
        foreach (var rb in new[] { NavScenarios, NavDepot, NavSettings })
            if (rb.IsChecked == true)
            {
                ShowPage(rb.Tag as string);
                return;
            }
        ShowPage("scenarios");
    }

    private void PopulateLangCombo()
    {
        LangCombo.Items.Clear();
        foreach (var lang in LocalizationService.AvailableLanguages())
            LangCombo.Items.Add(new ComboBoxItem { Content = lang.Name, Tag = lang.Code });
        if (LangCombo.ItemCount == 0)
            LangCombo.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });

        SelectActiveLanguage();
    }

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

        if (string.Equals(code, App.Loc.CurrentLangCode, StringComparison.OrdinalIgnoreCase))
            return;

        App.ApplyLanguage(code);
    }

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

        var keys = new MenuItem
        {
            Header = App.Loc["ShortcutsTitle"],
            InputGesture = new KeyGesture(Key.F1)
        };
        keys.Click += (_, _) => Views.ShortcutsWindow.Create().ShowDialog(this);
        menu.Items.Add(keys);
        menu.Items.Add(new Separator());

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

        // "About" belongs with the other informational entries, not next to the
        // Save/Reset buttons of the settings panel where it used to sit.
        menu.Items.Add(new Separator());
        var about = new MenuItem { Header = App.Loc["About"] };
        about.Click += (_, _) => Views.AboutWindow.Create().ShowDialog(this);
        menu.Items.Add(about);

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
                {
                    Diagnostics.ReportOnUiThread(string.Format(App.Loc["FaultFileNotFound"], target));
                    return;
                }
            }
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Open {pathOrUrl}", ex);
        }
    }

    private async void openLastLogClick(object? sender, RoutedEventArgs e)
    {
        string logPath = Path.GetFullPath("log.txt");
        if (!File.Exists(logPath))
        {
            await Diagnostics.ReportAsync(string.Format(App.Loc["FaultFileNotFound"], logPath));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await Diagnostics.ReportAsync(logPath, ex);
        }
    }

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

    private Task<bool> ShowExeProblem(ExeProblem problem, string exe)
    {
        string key = problem switch
        {
            ExeProblem.NotExecutable => "ExeNotExecutable",
            ExeProblem.WrongPlatform => "ExeWrongPlatform",
            _ => "ExeNotFound"
        };
        return MessageBox.Show(this, $"{exe}\n\n{App.Loc[key]}",
            App.Loc["ExeLaunchFailed"], MessageBoxButtons.Ok);
    }

    private async void StartButton_OnClick(object? sender, RoutedEventArgs e) =>
        await LaunchAsync(saveSettings: true, freeFly: false);

    private void StartButton_OnRightClick(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right)
            return;

        var menu = new ContextMenu();

        var noSave = new MenuItem { Header = App.Loc["StartWithoutSaving"] };
        noSave.Click += async (_, _) => await LaunchAsync(saveSettings: false, freeFly: false);
        menu.Items.Add(noSave);

        var freeFly = new MenuItem { Header = App.Loc["StartFreeFly"] };
        freeFly.Click += async (_, _) => await LaunchAsync(saveSettings: true, freeFly: true);
        menu.Items.Add(freeFly);

        menu.Open(startButton);
    }

    private async Task LaunchAsync(bool saveSettings, bool freeFly)
    {
        var scenery = AppState.Instance.CurrentScenery;
        if (scenery is null)
            return;
        var trainset = AppState.Instance.CurrentTrainset;
        if (trainset is null)
            return;

        if (!freeFly && !HasStartableVehicle(trainset) &&
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

        string? vehicle = TrainsetDisplay.UniquifyForLaunch(
            trainset, scenery, AppState.Instance.StartingVehicleName);
        if (string.IsNullOrEmpty(vehicle))
            vehicle = ResolveStartVehicle(trainset);
        AppState.Instance.StartingVehicleName = vehicle;

        LoadingScreen.Prepare(trainset.Logo, Path.GetFileNameWithoutExtension(scenery.Path));

        scenery.Weather.Dirty = true;

        string dir = Path.GetDirectoryName(scenery.Path) ?? "scenery";
        string exportName = "$" + Path.GetFileName(scenery.Path);
        string exportPath = Path.Combine(dir, exportName);
        try
        {
            File.WriteAllText(exportPath, scenery.BuildExportContent(SettingsModel.Instance.IgnoreIrrelevantTrains, AppServices.Current.Random), Encoding.GetEncoding(1250));
        }
        catch (Exception ex)
        {
            await Diagnostics.ReportAsync($"{exportPath}", ex, App.Loc["FaultTitle"]);
            return;
        }

        if (saveSettings)
            SettingsModel.Instance.CaptureAndSave();

        string exe = Path.GetFullPath(SettingsModel.Instance.ResolveExecutable(out var exeProblem));
        if (exeProblem != ExeProblem.None)
        {
            await ShowExeProblem(exeProblem, exe);
            return;
        }

        Process? sim;
        try
        {
            sim = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = freeFly ? $"-s {exportName}" : $"-s {exportName} -v {vehicle}",
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Directory.GetCurrentDirectory(),
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            await MessageBox.Show(this, $"{exe}\n\n{ex.Message}",
                App.Loc["ExeLaunchFailed"], MessageBoxButtons.Ok);
            return;
        }

        if (SettingsModel.Instance.AutoCloseStarter)
        {
            Close();
            return;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();

        WatchSimulator(sim);
    }

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

    private void WatchSimulator(Process? sim)
    {
        if (sim is null)
        {
            string name = Path.GetFileNameWithoutExtension(SettingsModel.Instance.ResolveExecutable());
            sim = Process.GetProcessesByName(name).FirstOrDefault();
        }

        if (sim is null)
        {

            Dispatcher.UIThread.Post(RestoreFromSimulator);
            return;
        }

        var watcher = new Thread(() =>
        {
            try { sim.WaitForExit(); }
            catch
            {
            }
            Dispatcher.UIThread.Post(RestoreFromSimulator);
        })
        {
            IsBackground = true,
            Name = "SimulatorWatcher"
        };
        watcher.Start();
    }

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

    private void RestoreFromSimulator()
    {
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
    }
}
