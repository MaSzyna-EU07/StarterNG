using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StarterNG.Classes;
using StarterNG.Infrastructure;
using StarterNG.Views;
using StarterNG.Application;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Settings;

namespace StarterNG;

public partial class MainWindow : Window
{

    private const int TitleBarHeight = 32;

    // The smallest window the pages are laid out for, as MainWindow.axaml sets it.
    private const double DesignMinWidth = 1010;
    private const double DesignMinHeight = 666;

    // The system frame, which a window's size does not count: at most 15 by 8 on Windows.
    private const double FrameAllowance = 16;

    // A start is over at once: a double click or a held key arrives after it.
    private static readonly TimeSpan StartCooldown = TimeSpan.FromSeconds(3);

    private DateTime _openedUtc = DateTime.UtcNow;
    private DateTime _startedUtc = DateTime.MinValue;
    private bool _starting;
    private bool _simulatorRunning;
    private bool _lookingAtSceneryFiles;
    private readonly DispatcherTimer _sceneryFilesTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public MainWindow()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        InitializeComponent();

        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = TitleBarHeight;

        FitToScreen();
        // Ahead of the layout a new size starts: changed during it, the bar keeps the old width.
        Resized += (_, e) => FitTopBar(e.ClientSize.Width);
        Opened += (_, _) => FitTopBar(ClientSize.Width);
        PageScroll.SizeChanged += (_, _) => KeepPagesUsable();

        if (AppServices.Current.Settings.WindowMaximized || wantsMaximized())
            WindowState = WindowState.Maximized;

        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty && WindowState != WindowState.Minimized)
                AppServices.Current.Settings.WindowMaximized = WindowState == WindowState.Maximized;
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

        AppServices.Current.State.Changed += UpdateStartButton;
        Opened += (_, _) =>
        {

            _openedUtc = DateTime.UtcNow;
            UpdateStartButton();
            _sceneryFilesTimer.Start();
        };
        Activated += (_, _) => CheckExternalSettings();
        _sceneryFilesTimer.Tick += async (_, _) => await LookAtSceneryFilesAsync();
        Closed += (_, _) => _sceneryFilesTimer.Stop();
    }

    /// <summary>
    /// Scenery files changed outside the starter are picked up without a reload. Not
    /// with the simulator running: it is left in peace, and what it changes comes in after it.
    /// </summary>
    private async Task LookAtSceneryFilesAsync()
    {
        if (_lookingAtSceneryFiles || _simulatorRunning || !IsVisible || WindowState == WindowState.Minimized)
            return;

        _lookingAtSceneryFiles = true;
        try
        {
            var services = AppServices.Current;
            var held = services.Library.Sceneries.ToList();
            var changes = await Task.Run(() => services.SceneryFiles.Look(held));
            if (changes.Count == 0)
                return;

            await ScenariosView.FollowSceneryFilesAsync(changes, mayAsk: IsActive);
            DepotView.FollowSceneryFiles();
        }
        catch (Exception ex)
        {
            Diagnostics.Log("Following the scenery files", ex);
        }
        finally
        {
            _lookingAtSceneryFiles = false;
        }
    }

    /// <summary>
    /// Holds the window to what the screen shows of it, so a small or heavily scaled
    /// one (1366x768, or 1080p at 150%) keeps Start above the taskbar. The system lets
    /// a window open larger than its screen, and centring one then puts its top off it.
    /// </summary>
    private void FitToScreen()
    {
        if ((Screens.ScreenFromPoint(Position) ?? Screens.Primary) is not { } screen)
            return;

        double width = screen.WorkingArea.Width / screen.Scaling - FrameAllowance;
        double height = screen.WorkingArea.Height / screen.Scaling - FrameAllowance;
        MinWidth = Math.Min(MinWidth, width);
        MinHeight = Math.Min(MinHeight, height);
        Width = Math.Min(Width, width);
        Height = Math.Min(Height, height);
    }

    /// <summary>
    /// Below the size the pages are laid out for they keep it and scroll, rather than
    /// squeeze their controls over each other. Start is outside this and stays in view.
    /// </summary>
    private void KeepPagesUsable()
    {
        double floorWidth = DesignMinWidth - (ClientSize.Width - PageScroll.Bounds.Width);
        double floorHeight = DesignMinHeight - (ClientSize.Height - PageScroll.Bounds.Height);
        bool narrow = PageScroll.Bounds.Width < floorWidth;
        bool low = PageScroll.Bounds.Height < floorHeight;

        PageHost.Width = narrow ? floorWidth : double.NaN;
        PageHost.Height = low ? floorHeight : double.NaN;
        PageScroll.HorizontalScrollBarVisibility = narrow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        PageScroll.VerticalScrollBarVisibility = low ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }

    // The version is in the About window too, and the logo is one more link. What fits goes
    // by what is there: the version's length depends on the build, the rest on the language.
    private void FitTopBar(double width)
    {
        static double Needed(Control control)
        {
            control.Measure(Size.Infinity);
            return control.DesiredSize.Width;
        }

        VersionPanel.IsVisible = true;
        LogoLink.IsVisible = true;
        double room = width - Needed(TopBarLeft);
        if (Needed(TopBarRight) > room)
            VersionPanel.IsVisible = false;
        if (Needed(TopBarRight) > room)
            LogoLink.IsVisible = false;
    }

    private void FitTopBarAfterLayout() =>
        Dispatcher.UIThread.Post(() => FitTopBar(ClientSize.Width), DispatcherPriority.Loaded);

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // A combo box opens on the release and leaves the press to pass on; a window
        // move started here would swallow that release.
        if (e.Source is Visual source && source.FindAncestorOfType<ComboBox>(includeSelf: true) is not null)
            return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void NavSettings_OnRightClick(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right)
            return;

        OpenLogAndTools();
        e.Handled = true;
    }

    public void OpenLogAndTools()
    {
        var win = new Views.LogAndToolsWindow();
        win.ToolRequested += async tool =>
        {
            switch (tool)
            {
                case "category": DepotView.ToolAddAllCategory(); break;
                case "mmd": DepotView.ToolAddUniqueMmd(); break;
                case "removeall": await DepotView.ToolRemoveAllTrainsAsync(); break;
            }
            ScenariosView.RefreshConsistView();
        };
        win.ShowDialog(this);
    }

    private void OnShortcutKey(object? sender, KeyEventArgs e)
    {
        // A cab-control binding in Settings is waiting for this very keypress.
        if (SettingsView?.IsCapturingKey == true)
            return;

        var mods = e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt);
        if (Shortcuts.Match(e.Key, mods) is not { } id)
            return;

        // Bare keys belong to whatever is being typed into, and so do undo, redo and
        // Ctrl+Delete, which there takes a word and not the whole consist; other
        // Ctrl/Alt combinations do not collide with text entry, so those stay live
        // everywhere.
        if ((mods == KeyModifiers.None || id is Shortcuts.Undo or Shortcuts.Redo or Shortcuts.ClearConsist) &&
            IsTextEntryFocused())
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
                    case Shortcuts.Undo: DepotView.Undo(); break;
                    case Shortcuts.Redo: DepotView.Redo(); break;
                    case Shortcuts.SaveToScenery: DepotView.KeySaveToScenery(); break;
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
        PageScroll.Offset = default;
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
        foreach (var lang in App.Loc.Languages())
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
        if ((LangCombo.SelectedItem as ComboBoxItem)?.Tag is string code)
            App.ChangeLanguage(code);
    }

    private void OnLanguageChanged()
    {
        SelectActiveLanguage();
        ScenariosView?.RebuildAfterLanguageChange();
        DepotView?.RebuildAfterLanguageChange();
        UpdateStartButton();
        FitTopBarAfterLayout();

        // A page not on the screen keeps what it measured: back in view, a tab strip
        // in a fixed column went on clipping its new headers at the old words' widths.
        foreach (var control in PageHost.GetVisualDescendants().OfType<Layoutable>())
            control.InvalidateMeasure();
    }

    private void HelpButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        menu.AddItem(App.Loc["ShortcutsTitle"], () => Views.ShortcutsWindow.Create().ShowDialog(this),
                     gesture: new KeyGesture(Key.F1));
        menu.Items.Add(new Separator());

        menu.AddItem(App.Loc["HelpWiki"], () => OpenPathOrUrl("https://wiki.eu07.pl/"));

        string readme = string.Equals(App.Loc.CurrentLangCode, "pl", StringComparison.OrdinalIgnoreCase)
            ? "readme.html" : "en-readme.html";
        if (File.Exists(readme) || File.Exists(Path.Combine(Directory.GetCurrentDirectory(), readme)))
            menu.AddItem(App.Loc["HelpReadme"], () => OpenPathOrUrl(readme));

        string regs = Path.Combine(Directory.GetCurrentDirectory(), "przepisy_kolejowe");
        if (Directory.Exists(regs))
        {
            menu.Items.Add(new Separator());
            foreach (string pdf in Directory.GetFiles(regs, "*.pdf").OrderBy(Path.GetFileName))
            {
                string file = pdf;
                menu.AddItem(Path.GetFileName(file), () => OpenPathOrUrl(file));
            }
        }

        menu.Items.Add(new Separator());
        menu.AddItem(App.Loc["LogAndTools"], OpenLogAndTools);

        // "About" belongs with the other informational entries, not next to the
        // Save/Reset buttons of the settings panel where it used to sit.
        menu.Items.Add(new Separator());
        var about = new MenuItem { Header = App.Loc["About"] };
        about.Click += (_, _) => Views.AboutWindow.Create().ShowDialog(this);
        menu.Items.Add(about);

        menu.Open(HelpButton);
    }

    private bool wantsMaximized()
    {
        // make sure the window fills the whole screen on gamescope sessions
        if (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") == "gamescope")
            return true;

        // catch being launched via Steam Big Picture mode as well
        if (Environment.GetEnvironmentVariable("SteamGamepadUI") == "1")
            return true;

        return false;
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

    private void openLastLogClick(object? sender, RoutedEventArgs e) =>
        Diagnostics.OpenOrReport(Path.GetFullPath("log.txt"));

    private void openHaslerTapesClick(object? sender, RoutedEventArgs e)
    {
        var window = new Views.Tapes.HaslerTapeWindow();
        window.LogEnabled += SettingsView.SyncLogs;
        window.Show(this);
    }

    private void UpdateStartButton()
    {
        if (startButton is null) return;

        var scenery = AppServices.Current.State.CurrentScenery;
        var trainset = AppServices.Current.State.CurrentTrainset;
        startEditorButton.Content = App.Loc[scenery is null ? "StartEditorWizard" : "StartEditorShort"];
        ToolTip.SetTip(startEditorButton, App.Loc[scenery is null ? "StartEditorWizardTip" : "StartEditor"]);
        startEditorMoreButton.IsVisible = scenery is not null;

        if (scenery is null)
        {
            startButton.Content = App.Loc["StartNoScenery"];
            startButton.IsEnabled = false;
            return;
        }
        if (trainset is null)
        {
            // Nothing to drive in this scenery - none listed, or none the filters
            // show: the free camera is what a start gives.
            startButton.Content = App.Loc["StartFreeFly"];
            ToolTip.SetTip(startButton, null);
            startButton.IsEnabled = true;
            return;
        }

        bool staffed = HasStartableVehicle(trainset);
        startButton.Content = staffed ? App.Loc["Start"] : App.Loc["StartNoStaff"];
        ToolTip.SetTip(startButton, staffed ? null : App.Loc["StartNoStaffConfirm"]);
        startButton.IsEnabled = true;
    }

    private static bool HasStartableVehicle(Trainset trainset) =>
        StartSimulation.HasCrewToStart(trainset, AppServices.Current.State.StartingVehicleName);

    private Task<bool> ShowExeProblem(ExeProblem problem, string exe) =>
        MessageBox.Show(this, $"{exe}\n\n{App.Loc[ExeProblemText.Key(problem)]}",
            App.Loc["ExeLaunchFailed"], MessageBoxButtons.Ok);

    private const string SimulatorBuildGuide = "https://github.com/MaSzyna-EU07/maszyna#getting-started";

    /// <summary>
    /// Tells a user whose processor lacks AVX2 that the published simulator will
    /// not run, and where the instructions for a build of their own are.
    /// </summary>
    public async Task ShowCpuNoticeAsync()
    {
        var (open, silence) = await MessageBox.ShowNotice(
            this, string.Format(App.Loc["CpuNoAvx2"], SimulatorBuildGuide), App.Loc["CpuNoAvx2Title"],
            App.Loc["CpuNoAvx2Open"], App.Loc["DontShowAgain"]);

        if (open)
            OpenPathOrUrl(SimulatorBuildGuide);
        if (silence)
        {
            AppServices.Current.Settings.CpuNotice = false;
            AppServices.Current.SettingsStore.Save();
        }
    }

    /// <summary>
    /// Asks for the simulator when the installation holds none, and keeps the pick
    /// straight away, so the question is not back on the next start.
    /// </summary>
    public async Task OfferExecutablePickAsync()
    {
        var services = AppServices.Current;
        string paragraph = Environment.NewLine + Environment.NewLine;
        string message = string.Format(App.Loc["FaultNoExe"], services.Executables.CanonicalName, services.Paths.Root) +
                         paragraph + App.Loc["FaultNoExePick"];
        if (!services.Environment.IsWindows)
            message += paragraph + App.Loc["FaultNoExeWine"];

        if (!await MessageBox.Show(this, message, App.Loc["FaultNoExeTitle"], MessageBoxButtons.YesNo,
                                   App.Loc["FaultNoExePickNow"], App.Loc["NotNow"]))
            return;

        NavSettings.IsChecked = true;
        if (await SettingsView.BrowseForExecutableAsync())
            SettingsView.Save();
    }

    private async void StartButton_OnClick(object? sender, RoutedEventArgs e) =>
        await LaunchAsync(saveSettings: true, freeFly: false);

    private void StartButton_OnRightClick(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right)
            return;

        OpenStartMenu(startButton);
    }

    private void StartMoreButton_OnClick(object? sender, RoutedEventArgs e) => OpenStartMenu(startMoreButton);

    private async void StartEditorButton_OnClick(object? sender, RoutedEventArgs e) => await LaunchEditorAsync();

    private void StartEditorMoreButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var create = new MenuItem { Header = App.Loc["StartEditorWizard"] };
        create.Click += async (_, _) => await FollowStartAsync(AppServices.Current.StartSimulation.ExecuteNewSceneryEditor);
        var menu = new ContextMenu();
        menu.Items.Add(create);
        menu.Open(startEditorMoreButton);
    }

    private void OpenStartMenu(Control anchor)
    {
        var menu = new ContextMenu();

        // Both need a consist to start in, as the start button itself does.
        menu.AddItem(App.Loc["StartWithoutSaving"], async () => await LaunchAsync(saveSettings: false, freeFly: false),
                     startButton.IsEnabled);
        menu.AddItem(App.Loc["StartFreeFly"], async () => await LaunchAsync(saveSettings: true, freeFly: true),
                     startButton.IsEnabled);

        menu.Open(anchor);
    }

    /// <summary>
    /// Runs the start use case and turns whatever it reports into something the
    /// user sees. Everything that is not a dialog lives in <see cref="StartSimulation"/>.
    /// </summary>
    private async Task LaunchAsync(bool saveSettings, bool freeFly)
    {
        // A second press while the first is still on its way, or right after it,
        // would start a second simulator.
        if (_starting || DateTime.UtcNow - _startedUtc < StartCooldown)
            return;

        _starting = true;
        try
        {
            var scenery = AppServices.Current.State.CurrentScenery;
            var trainset = AppServices.Current.State.CurrentTrainset;
            if (scenery is null)
                return;
            freeFly |= trainset is null;

            if (!freeFly && trainset != null && !HasStartableVehicle(trainset) &&
                !await MessageBox.Show(this, App.Loc["StartNoStaffConfirm"],
                                       App.Loc["StartNoStaffTitle"], MessageBoxButtons.YesNo))
                return;

            LoadingScreen.Prepare(trainset?.Logo, Path.GetFileNameWithoutExtension(scenery.Path));

            await FollowStartAsync(() => AppServices.Current.StartSimulation.Execute(freeFly, saveSettings));
        }
        finally
        {
            _starting = false;
        }
    }

    /// <summary>
    /// The scenery editor, which only some simulator builds have - said up front,
    /// since one without it simply does not open it, and it writes to the scenery
    /// itself. With none selected there is nothing to write to: the simulator's
    /// wizard makes a new one.
    /// </summary>
    private async Task LaunchEditorAsync()
    {
        if (AppServices.Current.State.CurrentScenery is { } scenery &&
            !await MessageBox.Show(this, string.Format(App.Loc["StartEditorWarning"], Path.GetFileName(scenery.Path)),
                                   App.Loc["StartEditor"], MessageBoxButtons.YesNo))
            return;

        await FollowStartAsync(AppServices.Current.StartSimulation.ExecuteEditor);
    }

    /// <summary>Reports a start that failed, or steps aside for the simulator that started.</summary>
    /// <param name="start">The start, run again once what stopped it is put right.</param>
    private async Task FollowStartAsync(Func<SimulationStartResult> start)
    {
        var result = start();
        switch (result.Outcome)
        {
            case SimulationStartOutcome.NothingSelected:
                return;

            case SimulationStartOutcome.ExportFailed:
                await Diagnostics.ReportAsync(
                    $"{result.ExecutablePath}{Environment.NewLine}{Environment.NewLine}{result.Detail}",
                    App.Loc["FaultTitle"]);
                return;

            case SimulationStartOutcome.ExecutableProblem:
                await ShowExeProblem(result.Problem, result.ExecutablePath);
                return;

            case SimulationStartOutcome.LaunchFailed:
                await MessageBox.Show(this, $"{result.ExecutablePath}\n\n{result.Detail}",
                                      App.Loc["ExeLaunchFailed"], MessageBoxButtons.Ok);
                return;
        }

        _startedUtc = DateTime.UtcNow;

        if (AppServices.Current.Settings.AutoCloseStarter)
        {
            Close();
            return;
        }

        // The starter sits idle while the simulator runs; give the memory back.
        GC.Collect();
        GC.WaitForPendingFinalizers();

        WatchSimulator(result.Process, result.ExecutablePath);
    }

    /// <summary>
    /// Brings the starter back when the simulator exits. Adopts an already
    /// running simulator when we did not start it ourselves.
    /// </summary>
    private void WatchSimulator(IProcessHandle? simulator, string executable)
    {
        var started = DateTime.UtcNow;
        simulator ??= AppServices.Current.Processes.FindRunning(
            Path.GetFileNameWithoutExtension(AppServices.Current.SettingsStore.ResolveExecutable()));

        if (simulator is null)
        {
            Dispatcher.UIThread.Post(RestoreFromSimulator);
            return;
        }

        _simulatorRunning = true;
        _ = simulator.WaitForExitAsync().ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            _simulatorRunning = false;
            RestoreFromSimulator();
            ReportIfCrashed(simulator, executable, started);
        }), TaskScheduler.Default);
    }

    /// <summary>
    /// Says why the simulator is gone when it did not end the way a session does:
    /// an error code, or a close so soon after the start that nobody played - a
    /// window that only flashed. Its own words come from the error stream, or from
    /// its log when that stayed silent, and a minidump it left is pointed at.
    /// </summary>
    private async void ReportIfCrashed(IProcessHandle simulator, string executable, DateTime startedUtc)
    {
        if (simulator.ExitCode is not { } code)
            return;

        var runTime = DateTime.UtcNow - startedUtc;

        var exit = new SimulatorExit(code, runTime, simulator.ErrorTail);
        if (!exit.Failed)
            return;

        var words = exit.CleanErrors.Count > 0
            ? exit.CleanErrors
            : AppServices.Current.Aftermath.SimulatorLogTail(12, startedUtc);

        var message = new StringBuilder();
        message.AppendLine(string.Format(App.Loc["SimExitCode"], exit.DescribeCode()));
        if (exit.Quick)
            message.AppendLine(string.Format(App.Loc["SimExitQuick"], runTime.TotalSeconds));
        if (words.Count > 0)
        {
            message.AppendLine().AppendLine(App.Loc["SimExitMessages"]);
            foreach (string line in words)
                message.AppendLine(line);
        }
        message.AppendLine().Append(App.Loc["SimExitSeeLog"]);

        string? dump = AppServices.Current.Aftermath.CrashDumpsSince(startedUtc, executable).FirstOrDefault();
        if (dump is not null)
            message.AppendLine().AppendLine().AppendLine(string.Format(App.Loc["SimExitDump"], dump))
                   .Append(App.Loc["SimExitOpenDump"]);

        Diagnostics.Log(message.ToString().Replace(Environment.NewLine, " | "));
        bool open = await MessageBox.Show(this, message.ToString(), App.Loc["SimExitTitle"],
                                          dump is null ? MessageBoxButtons.Ok : MessageBoxButtons.YesNo);
        if (open && dump is not null && Path.GetDirectoryName(Path.GetFullPath(dump)) is { } folder)
            AppServices.Current.Processes.OpenInShell(folder);
    }

    private async void CheckExternalSettings()
    {
        if ((DateTime.UtcNow - _openedUtc).TotalSeconds < 3)
            return;
        if (!AppServices.Current.SettingsStore.IsConfigNewerOnDisk())
            return;

        var result = await MessageBox.Show(
            this,
            App.Loc["SettingsFileChanged"],
            App.Loc["SettingsFileChangedTitle"],
            MessageBoxButtons.YesNo);
        if (result)
        {
            AppServices.Current.SettingsStore.Load();
            SettingsView?.ReloadFromSettings();
        }
        else
            AppServices.Current.SettingsStore.TouchConfigAge();
    }

    private void RestoreFromSimulator()
    {
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = wantsMaximized() ? WindowState.Maximized : WindowState.Normal;
    }
}
