using System;
using System.Collections.Generic;
using System.Globalization;
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
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using StarterNG.Classes;
using StarterNG.Controls;
using StarterNG.Infrastructure;

using StarterNG.Domain.Settings;
using StarterNG.Application;

namespace StarterNG.Views;

public partial class Settings : UserControl, ISettingsCapture
{
    private bool _loading;

    private readonly SimulatorKeyRows _keyRows = new();

    private readonly KeyBindingsTab _keyBindings;

    private readonly SliderSteps _fpsLimit;

    private readonly SliderSteps _reflections;

    private readonly SliderSteps _anisotropy;

    private readonly SliderSteps _multisampling;

    private readonly SliderSteps _drawRangeFactor;

    // False while the layout loads: the handlers it names fire as it sets their controls'
    // values, before the fields of the controls are there, and do nothing until this is set.
    private bool _built;

    /// <summary>
    /// Raised once a saved (or applied) setting changes something the other tabs
    /// have already drawn, so they can redraw instead of waiting for a restart.
    /// </summary>
    public event Action? ThumbnailSizeChanged;

    /// <summary>Thumbnail size the other tabs were last drawn at.</summary>
    private bool _drawnThumbs = AppServices.Current.Settings.LargeThumbnails;

    public Settings()
    {
        InitializeComponent();

        _fpsLimit = new SliderSteps(FpsLimitSlider, Steps.FrameRateLimits);
        _reflections = new SliderSteps(reflectionsFramerate, Steps.ReflectionUpdates);
        _anisotropy = new SliderSteps(TexFilteringSlider, Steps.Anisotropy);
        _multisampling = new SliderSteps(MultisamplingSlider, Steps.MultisamplingLevels);
        _drawRangeFactor = new SliderSteps(RenderRangeSlider, Steps.DrawRangeFactors);
        _built = true;

        SettingsPathText.Text = AppServices.Current.SettingsStore.LoadedFrom;
        ToolTip.SetTip(SettingsPathText, AppServices.Current.SettingsStore.LoadedFrom);

        this.AttachedToVisualTree += (_, _) =>
        {
            ShowPixels(TexResolutionRow, textureResolutionSlider);
            ShowPixels(CabTexResolutionRow, cabTextureResolutionSlider);
            UpdateShadowResolution();
            UpdateQualityCaptions();
            // Only now is it known which window, and so which monitors, this is on.
            RefreshResolutions();
            if (TopLevel.GetTopLevel(this)?.Screens is { } screens)
                screens.Changed += (_, _) => RefreshResolutions();
        };

        FillLanguageCombo();

        App.Loc.LanguageChanged += SelectActiveLanguage;
        App.Loc.LanguageChanged += UpdateQualityCaptions;
        App.Loc.LanguageChanged += UpdateSaveState;
        App.Loc.LanguageChanged += UpdateShadowResolution;
        App.Loc.LanguageChanged += RefreshResolutions;
        App.Loc.LanguageChanged += () =>
        {
            if (SelectExeCb.Items.Count > 0 && SelectExeCb.Items[0] is ComboBoxItem auto)
                auto.Content = App.Loc["SelectEXEAuto"];
        };
        _keyBindings = new KeyBindingsTab(ControlsHost, SettingsSearch);
        _keyBindings.Changed += MarkDirty;
        App.Loc.LanguageChanged += _keyBindings.Build;

        _keyRows.Build(SettingsStack);
        _keyRows.Changed += MarkDirty;
        _keyRows.Changed += QueuePresetMarks;
        BuildPresetButtons();
        BuildMonitorRates();
        App.Loc.LanguageChanged += _keyRows.UpdateTexts;

        FeedbackCb.SelectionChanged += FeedbackCb_OnSelectionChanged;
        SelectExeCb.SelectionChanged += (_, _) => UpdateIdleNotes();
        FullscreenWindowedCb.IsCheckedChanged += (_, _) => UpdateIdleNotes();
        App.Loc.LanguageChanged += UpdateIdleNotes;
        App.Loc.LanguageChanged += UpdateGpuNote;
        // targetfps decides whether the draw range factor does anything.
        _keyRows.Moved += key =>
        {
            if (string.Equals(key.Key, "targetfps", StringComparison.OrdinalIgnoreCase))
                UpdateIdleNotes();
        };
        MotionBlurCb.IsCheckedChanged += (_, _) => UpdateDependentRows();
        ReflectionsCubeMapCb.IsCheckedChanged += (_, _) => UpdateDependentRows();

        AppServices.Current.SettingsStore.RegisterCapture(this);

        HookUart();
        ProfileCb.SelectionChanged += (_, _) => UpdateProfileState();
        App.Loc.LanguageChanged += UpdateProfileState;

        ApplyToUi();

        KeyboardConfig.Instance.Load();
        _keyBindings.Build();
        AddHandler(KeyDownEvent, _keyBindings.OnKeyDown, RoutingStrategies.Tunnel);

        HookDirtyTracking();
        HookSectionResets();
        ApplyFilter();
        UpdateSaveState();
    }

    private static int IndexOfTag(ComboBox cb, string? tag)
    {
        for (int i = 0; i < cb.Items.Count; i++)
            if (cb.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private static int FindComboIndexByContent(ComboBox cb, string content)
    {
        for (int i = 0; i < cb.Items.Count; i++)
            if (cb.Items[i] is ComboBoxItem item &&
                string.Equals(item.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private void PopulateProfileCombo()
    {
        string? current = (ProfileCb.SelectedItem as ComboBoxItem)?.Content?.ToString();
        ProfileCb.Items.Clear();
        foreach (string name in SettingsProfileStore.ListProfiles())
            ProfileCb.Items.Add(new ComboBoxItem { Content = name });
        if (current is not null)
        {
            int idx = FindComboIndexByContent(ProfileCb, current);
            if (idx >= 0) ProfileCb.SelectedIndex = idx;
        }
        UpdateProfileState();
    }

    /// <summary>Nothing to apply says so, rather than a button that does nothing.</summary>
    private void UpdateProfileState()
    {
        ProfileCb.PlaceholderText = App.Loc[ProfileCb.ItemCount == 0 ? "ProfileNone" : "ProfilePick"];
        ProfileApplyBtn.IsEnabled = ProfileCb.SelectedItem is not null;
    }

    /// <summary>Applied means in eu07.ini, the file the simulator goes by, and on this screen.</summary>
    private void ProfileApplyButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ProfileCb.SelectedItem is not ComboBoxItem { Content: string name } || string.IsNullOrWhiteSpace(name))
            return;

        string path = SettingsProfileStore.PathFor(name);
        var store = AppServices.Current.SettingsStore;
        bool loaded = store.TryLoadProfile(path);
        ApplyToUi();
        if (!loaded)
        {
            SaveStatus.Text = string.Format(App.Loc["ProfileNotApplied"], name);
            return;
        }

        Save();
        if (!store.LastSaveFailed)
            SaveStatus.Text = string.Format(App.Loc["ProfileApplied"], name);
    }

    private void ProfileSaveAsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = App.Loc["ProfileNamePrompt"], MinWidth = 220 };
        var ok = new Button
        {
            Content = App.Loc["Save"],
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinWidth = 90
        };
        ok.Classes.Add("Flat");
        ok.Classes.Add("Accent");
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(8), MinWidth = 240 };
        panel.Children.Add(new TextBlock { Text = App.Loc["ProfileNamePrompt"], FontWeight = FontWeight.Bold, FontSize = 12 });
        panel.Children.Add(nameBox);
        panel.Children.Add(ok);
        var flyout = new Flyout { Content = panel };

        async void Commit()
        {
            string name = nameBox.Text?.Trim() ?? string.Empty;
            if (name.Length == 0) return;
            flyout.Hide();
            await SaveProfileAsync(name);
        }
        ok.Click += (_, _) => Commit();
        nameBox.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) { ev.Handled = true; Commit(); } };
        flyout.ShowAt(ProfileSaveAsBtn, showAtPointer: false);
        nameBox.Focus(); nameBox.SelectAll();
    }

    private async Task SaveProfileAsync(string name)
    {
        string path = SettingsProfileStore.PathFor(name);
        if (File.Exists(path) && TopLevel.GetTopLevel(this) is Window window &&
            !await MessageBox.Show(window, string.Format(App.Loc["ProfileOverwrite"], name),
                                   App.Loc["ProfileSaveAs"], MessageBoxButtons.YesNo))
            return;

        var store = AppServices.Current.SettingsStore;
        CaptureInto(AppServices.Current.Settings);
        store.SaveTo(path);
        PopulateProfileCombo();
        int idx = FindComboIndexByContent(ProfileCb, Path.GetFileNameWithoutExtension(path));
        if (idx >= 0) ProfileCb.SelectedIndex = idx;
        SaveStatus.Text = string.Format(App.Loc[store.LastSaveFailed ? "ProfileNotSaved" : "ProfileSaved"], name);
    }

    private void PopulateExeCombo()
    {
        string? current = (SelectExeCb.SelectedItem as ComboBoxItem)?.Content?.ToString();
        SelectExeCb.Items.Clear();
        SelectExeCb.Items.Add(new ComboBoxItem { Content = App.Loc["SelectEXEAuto"] });
        foreach (string exe in AppServices.Current.Executables.ListCandidates())
            SelectExeCb.Items.Add(new ComboBoxItem { Content = exe });
        if (current is not null)
        {
            int idx = FindComboIndexByContent(SelectExeCb, current);
            SelectExeCb.SelectedIndex = idx >= 0 ? idx : 0;
        }
    }

    private async void BrowseExeButton_OnClick(object? sender, RoutedEventArgs e) =>
        await BrowseForExecutableAsync();

    /// <summary>
    /// Picks the simulator from anywhere, such as a build tree kept beside the data,
    /// where the automatic search - the working directory only - cannot see it.
    /// True when a file was picked; like any other choice here, it waits for Save.
    /// </summary>
    public async Task<bool> BrowseForExecutableAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return false;

        var picked = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = App.Loc["SelectEXEBrowseTitle"],
            AllowMultiple = false
        });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } path)
            return false;

        var problem = AppServices.Current.Executables.Validate(path);
        string message = $"{path}\n\n{App.Loc[ExeProblemText.Key(problem)]}";
        if (problem == ExeProblem.WrongPlatform)
        {
            // Not a dead end: Wine or Proton runs a Windows build on Linux.
            if (!await MessageBox.Show(owner, $"{message}\n\n{App.Loc["SelectEXEUseAnyway"]}",
                                       App.Loc["SelectEXE"], MessageBoxButtons.YesNo))
                return false;
        }
        else if (problem != ExeProblem.None)
        {
            await MessageBox.Show(owner, message, App.Loc["SelectEXE"], MessageBoxButtons.Ok);
            return false;
        }

        // Inside the installation the bare name is enough, and matches the list.
        string root = Path.GetFullPath(AppServices.Current.Paths.Root);
        string entry = string.Equals(Path.GetDirectoryName(path), root, StringComparison.Ordinal)
            ? Path.GetFileName(path)
            : path;

        SelectExeEntry(entry);
        return true;
    }

    /// <summary>Selects the simulator of that name, joining the list if the list lacks it.</summary>
    private void SelectExeEntry(string entry)
    {
        int index = FindComboIndexByContent(SelectExeCb, entry);
        if (index < 0)
        {
            SelectExeCb.Items.Add(new ComboBoxItem { Content = entry });
            index = SelectExeCb.Items.Count - 1;
        }
        SelectExeCb.SelectedIndex = index;
    }

    public void ReloadFromSettings() => ApplyToUi();

    /// <summary>
    /// True while a cab-control binding is waiting for a keypress. The window's own
    /// shortcuts stand down for the duration, otherwise Ctrl+N would never reach here.
    /// </summary>
    public bool IsCapturingKey => _keyBindings.IsCapturing;

    /// <summary>Switched on from the tapes' window: the boxes follow, as no edit of their own.</summary>
    public void SyncLogs() =>
        WhileLoading(() =>
        {
            SpeedometerTapesCb.IsChecked = AppServices.Current.Settings.PhysicsLog;
            SimLogsCb.IsChecked = AppServices.Current.Settings.DebugLog;
        });

    /// <summary>Runs <paramref name="change"/> as no edit of the user's, from wherever it is called.</summary>
    private void WhileLoading(Action change)
    {
        bool loading = _loading;
        _loading = true;
        try
        {
            change();
        }
        finally
        {
            _loading = loading;
        }
    }

    private void ApplyToUi()
    {
        var s = AppServices.Current.Settings;
        _loading = true;
        try
        {
            PopulateProfileCombo();
            PopulateExeCombo();

            SelectActiveLanguage();
            ShowValues(s);
            _keyRows.Apply(s);
        }
        finally
        {
            _loading = false;
        }

        // A reloaded file may name another simulator, or start a server.
        RefreshDerivedRows();
        UpdateGpuNote();
        NoteShownValues();
        UpdatePresetMarks();
    }

    /// <summary>
    /// Puts <paramref name="s"/> into the controls of this screen - all but the
    /// language, the profiles and the rows of <see cref="SimulatorKeyRows"/>.
    /// </summary>
    private void ShowValues(SimulatorSettings s)
    {
        FullscreenCb.IsChecked = s.Fullscreen;
        PauseInactiveCb.IsChecked = s.PauseWhenInactive;
        PauseStartCb.IsChecked = s.PauseOnStart;
        CursorSensitivitySlider.Value = s.CursorSensitivity;
        MouseHorInvertCb.IsChecked = s.InvertMouseHorizontal;
        MouseVertInvertCb.IsChecked = s.InvertMouseVertical;

        GamepadIgnoreCb.IsChecked = s.IgnoreGamepad;
        FeedbackCb.SelectedIndex = s.FeedbackMode;
        FeedbackPortNud.Value = (decimal)s.FeedbackPort;
        ShowUart(s);
        UpdateFeedbackDetailVisibility();

        if (s.SelectExeAutomatically)
        {
            SelectExeCb.SelectedIndex = 0;
        }
        else
        {
            SelectExeEntry(s.ExecutablePath);
        }
        DebugModeCb.IsChecked = s.DebugMode;
        VirtualShuntingCb.IsChecked = s.VirtualShunting;
        LogMissingVehicleFilesCb.IsChecked = s.LogMissingVehicleFiles;

        RenderEngineCb.SelectedIndex = s.RenderEngine;
        GpuCb.SelectedIndex = s.Gpu == GpuPreference.System ? 1 : 0;
        SelectResolution(s.Width, s.Height);
        bufferScale.Value = s.BufferScalePercent;
        textureResolutionSlider.Value = Log2(s.MaxTextureSize, 12);
        cabTextureResolutionSlider.Value = Log2(s.MaxCabTextureSize, 12);
        _anisotropy.Value = s.TextureFiltering;
        _multisampling.Value = s.Multisampling;
        DynamicLightsSlider.Value = s.DynamicLights;
        _drawRangeFactor.Value = s.DrawRangeFactor;
        VSyncCb.IsChecked = s.VSync;
        SmokeDisplayCb.IsChecked = s.Smoke;
        SmokeParticlesSlider.Value = s.SmokeFidelity;
        ChromaticAberrationCb.IsChecked = s.ChromaticAberration;
        MotionBlurCb.IsChecked = s.MotionBlur;
        AdditionalShadersCb.IsChecked = s.ExtraEffects;
        ReflectionsCubeMapCb.IsChecked = s.EnvMap;
        RenderVBOCb.IsChecked = s.UseVbo;
        RenderShadowsCb.IsChecked = s.RenderShadows;
        _reflections.Value = s.ReflectionsFramerate;
        shaderResolutionSlider.Value = Log2(s.ShadowMapResolution, 12);
        shaderRange.Value = s.ShadowProjectionRange;
        cabShaderSourceRange.Value = s.CabShadowsRange;
        ShadowDisplayCb.SelectedIndex = Math.Clamp(s.ShadowRankCutoff - 1, 0, 2);
        ReflectionsDetailsCb.SelectedIndex = s.ReflectionsFidelity;
        fovSlider.Value = s.FieldOfView;
        RenderScreensCb.IsChecked = s.PythonScreens;
        RenderScreensThreadCb.IsChecked = s.PythonThreadedUpload;
        _fpsLimit.Value = s.FpsLimitEnabled ? s.FpsLimit : 0;
        ShadowAngleLimitSlider.Value = s.ShadowAngleLimit;
        FullscreenWindowedCb.IsChecked = s.FullscreenWindowed;
        RenderAngleVulkanCb.IsChecked = s.RenderAngleVulkan;

        TrackCurvesSlider.Value = s.SplineFidelity;
        PhysicsAccuracyCb.IsChecked = s.FullPhysics;
        PantographBreakCb.IsChecked = s.EnableTraction;
        OverheadOnlyCb.IsChecked = s.LiveTraction;
        SpeedometerTapesCb.IsChecked = s.PhysicsLog;
        SimLogsCb.IsChecked = s.DebugLog;
        KeepLogsCb.IsChecked = s.MultipleLogs;
        DisplaySimulationCb.IsChecked = s.DisplaySimulation;
        CrashDamageCb.IsChecked = s.CrashDamage;
        FrictionSlider.Value = s.Friction;
        BrakeStepSlider.Value = s.BrakeStep;
        BrakeSpeedSlider.Value = s.BrakeSpeed;

        EnableSoundsCb.IsChecked = s.SoundEnabled;
        VolumeSlider.Value = s.Volume;
        RadioVolumeSlider.Value = s.RadioVolume;
        VehiclesVolumeSlider.Value = s.VehiclesVolume;
        PositionalVolumeSlider.Value = s.PositionalVolume;
        AmbientVolumeSlider.Value = s.AmbientVolume;
        SkipPipelineCb.IsChecked = s.SkipPipeline;
        DebugLogVisibleCb.IsChecked = s.DebugLogVisible;
        PyScreenPriorityCb.SelectedIndex = Math.Max(0, IndexOfTag(PyScreenPriorityCb, s.PythonScreenUpdateRate.ToString(CultureInfo.InvariantCulture)));

        AutoCloseStarterCb.IsChecked = s.AutoCloseStarter;
        LargeThumbnailsCb.IsChecked = s.LargeThumbnails;
        AutoExpandTreeCb.IsChecked = s.AutoExpandSceneryTree;
        ReadyOverrideCb.SelectedIndex = (int)s.ReadyOverride;

        CompressTexCb.IsChecked = s.CompressTextures;
        ScaleSpecularsCb.IsChecked = s.ScaleSpeculars;
        UseGlesCb.IsChecked = s.UseGLES;
        ShaderGammaCb.IsChecked = s.ShaderGamma;
        ScreenMipmapsCb.IsChecked = s.ScreenMipmaps;
        ConvertModelsCb.IsChecked = s.ExtendedModelConversion;
        ResourceMoveCb.IsChecked = s.GfxResourceMove;
        ResourceSweepCb.IsChecked = s.GfxResourceSweep;
        TrainThreadsSlider.Maximum = Math.Max(1, Environment.ProcessorCount);
        TrainThreadsSlider.Value = s.TrainPhysicsThreads;
        IgnoreIrrelevantCb.IsChecked = s.IgnoreIrrelevantTrains;
    }

    /// <summary>
    /// Puts <paramref name="s"/> on the screen as edits waiting for Save: what it
    /// changes marks the screen dirty, as a change by hand would.
    /// </summary>
    private void ShowAsEdits(SimulatorSettings s)
    {
        ShowValues(s);
        RefreshDerivedRows();
    }

    /// <summary>What on the screen follows the values shown, and is not one of them.</summary>
    private void RefreshDerivedRows()
    {
        UpdateQualityCaptions();
        UpdateIdleNotes();
        UpdateDependentRows();
    }

    // ── Levels of detail ─────────────────────────────────────────────────────

    private readonly List<(GraphicsPreset Preset, Button Button)> _presetButtons = new();

    private bool _presetMarksQueued;

    private void BuildPresetButtons()
    {
        foreach (var preset in GraphicsPreset.All)
        {
            var button = new Button { Cursor = new Cursor(StandardCursorType.Hand) };
            button.Classes.Add("Flat");
            button.Click += (_, _) => ApplyPreset(preset);
            PresetButtons.Children.Add(button);
            _presetButtons.Add((preset, button));
        }

        foreach (var (section, group) in new[]
                 {
                     (DrawRangeSection, GraphicsGroup.DrawRange), (TexturesSection, GraphicsGroup.Textures),
                     (EffectsSection, GraphicsGroup.Effects), (ShadowsSection, GraphicsGroup.Shadows),
                     (ReflectionsSection, GraphicsGroup.Reflections)
                 })
        {
            var levels = new ComboBox();
            levels.Classes.Add("SectionLevel");
            levels.SelectionChanged += (_, _) =>
            {
                if (!_markingLevels && levels.SelectedItem is ComboBoxItem { Tag: GraphicsPreset level })
                    ApplyGroupLevel(group, level);
            };
            section.Level = levels;
            _groupLevels.Add((group, levels));
        }

        UpdatePresetTexts();
        App.Loc.LanguageChanged += UpdatePresetTexts;
    }

    private void UpdatePresetTexts()
    {
        foreach (var (preset, button) in _presetButtons)
        {
            button.Content = App.Loc[preset.NameKey];
            ToolTip.SetTip(button, SettingTexts.PresetSummary(preset));
        }

        _markingLevels = true;
        foreach (var (group, levels) in _groupLevels)
        {
            levels.Items.Clear();
            foreach (var level in GraphicsPreset.All)
                levels.Items.Add(new ComboBoxItem { Content = App.Loc[level.NameKey], Tag = level });
            levels.PlaceholderText = App.Loc["PresetCustom"];
            ToolTip.SetTip(levels, App.Loc["GfxGroupLevelTip"]);
        }
        _markingLevels = false;

        UpdatePresetMarks();
    }

    private readonly GraphicsPresetMemory _presetMemory = new();

    private readonly List<(GraphicsGroup Group, ComboBox Levels)> _groupLevels = new();

    private GraphicsGroupLevels? _groups;

    private GraphicsGroupLevels Groups => _groups ??= new GraphicsGroupLevels(AppServices.Current.SettingsStore);

    // The screen following the values, not the player choosing.
    private bool _markingLevels;

    /// <summary>Puts a level of detail on the screen, as edits waiting for Save.</summary>
    private void ApplyPreset(GraphicsPreset preset)
    {
        var trial = Trial();
        var applied = _presetMemory.Apply(preset, trial);
        ShowAsEdits(trial);
        foreach (string key in applied.RowKeys)
            _keyRows.Show(key, trial.Extra[key]);

        // Chosen as a whole, its values are choices, not ones the screen could not show.
        ForgetShown(applied.ChosenKeys);
        UpdatePresetMarks();
    }

    /// <summary>Puts one part at a level, as edits waiting for Save.</summary>
    private void ApplyGroupLevel(GraphicsGroup group, GraphicsPreset level)
    {
        var trial = Groups.Apply(group, level, Trial());
        ShowAsEdits(trial);
        foreach (string key in group.Keys.Where(key => SimulatorKeys.All.Any(known => known.Key == key)))
            _keyRows.Show(key, SimulatorKeys.Named(key).RawIn(trial));

        ForgetShown(group.Keys);
        UpdatePresetMarks();
    }

    /// <summary>Marks the level the screen is at, once the controls moving now settle.</summary>
    private void QueuePresetMarks()
    {
        if (_presetMarksQueued)
            return;
        _presetMarksQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _presetMarksQueued = false;
            UpdatePresetMarks();
        }, DispatcherPriority.Background);
    }

    private void UpdatePresetMarks()
    {
        var trial = Trial();
        var matching = GraphicsPreset.Matching(trial);
        foreach (var (preset, button) in _presetButtons)
        {
            button.Classes.Set("Accent", preset == matching);
            button.Classes.Set("Flat", preset != matching);
        }
        PresetStateText.Text = matching is null ? App.Loc["PresetCustom"] : string.Empty;

        var groupLevels = Groups.LevelsOf(trial);
        _markingLevels = true;
        foreach (var (group, levels) in _groupLevels)
            levels.SelectedItem = levels.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => ReferenceEquals(item.Tag, groupLevels[group]));
        _markingLevels = false;
    }

    // ── The screens' refresh rates, for the frame rate limit ─────────────────

    private static readonly IBrush FgDimBrush = new SolidColorBrush(Color.Parse("#9098A0"));

    private void BuildMonitorRates()
    {
        var displays = AppServices.Current.Displays.List();
        if (displays.Count == 0)
            return;

        MonitorRates.IsVisible = true;
        UpdateMonitorRates(displays);
        App.Loc.LanguageChanged += () => UpdateMonitorRates(displays);
    }

    private void UpdateMonitorRates(IReadOnlyList<Display> displays)
    {
        MonitorRates.Children.Clear();
        foreach (var display in displays)
        {
            string screen = string.IsNullOrEmpty(display.Name)
                ? string.Format(App.Loc["MonitorRate"], display.RefreshRate)
                : string.Format(App.Loc["MonitorRateNamed"], display.Name, display.RefreshRate);
            MonitorRates.Children.Add(new TextBlock
            {
                Text = screen, FontSize = 11, Foreground = FgDimBrush, VerticalAlignment = VerticalAlignment.Center
            });
            var match = new Button
            {
                Content = string.Format(App.Loc["MonitorRateUse"], display.RefreshRate),
                Margin = new Thickness(4, 0, 14, 0)
            };
            match.Classes.Add("Outline");
            match.Classes.Add("SectionReset");
            int rate = display.RefreshRate;
            match.Click += (_, _) => _fpsLimit.Value = rate;
            MonitorRates.Children.Add(match);
        }
    }

    // ── Sections back to the simulator's own values ──────────────────────────

    // The language and the simulator are choices, not settings with a default;
    // the uart key holds the port and the speed of a board that is plugged in.
    private static readonly HashSet<string> KeptOnReset = new(StringComparer.OrdinalIgnoreCase)
    {
        "lang", "starter.exe.path", "uart"
    };

    private readonly List<SettingsSection> _resettable = new();

    private void HookSectionResets()
    {
        foreach (var section in SettingsStack.GetLogicalDescendants().OfType<SettingsSection>())
        {
            if (ResettableKeys(section).Count == 0)
                continue;
            section.ResetRequested += ResetSection;
            _resettable.Add(section);
        }

        UpdateResetLabels();
        App.Loc.LanguageChanged += UpdateResetLabels;
    }

    private void UpdateResetLabels()
    {
        foreach (var section in _resettable)
            section.ResetLabel = App.Loc["SectionDefaults"];
    }

    /// <summary>The keys of the section's rows and switch that it can put back.</summary>
    private static HashSet<string> ResettableKeys(SettingsSection section)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(section.ToggleKey))
            keys.Add(section.ToggleKey);
        foreach (var row in section.GetLogicalDescendants().OfType<SettingRow>())
            foreach (string key in (row.ConfigKey ?? string.Empty)
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                keys.Add(key);
        keys.ExceptWith(KeptOnReset);
        return keys;
    }

    /// <summary>Puts the section's rows at the simulator's own values, as edits waiting for Save.</summary>
    private void ResetSection(SettingsSection section)
    {
        var keys = ResettableKeys(section);
        ShowAsEdits(AppServices.Current.SettingsStore.WithDefaults(Trial(), keys));
        foreach (string key in keys)
            _keyRows.ShowDefault(key);

        // Put back by hand, a value is a choice like any other.
        ForgetShown(keys);
    }

    // ── Values the screen shows otherwise than the file has them ─────────────

    private IReadOnlyDictionary<string, ShownValue> _shownUnlikeSettings = new Dictionary<string, ShownValue>();

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ShownValue> ShownUnlikeSettings => _shownUnlikeSettings;

    /// <summary>
    /// Notes the keys a control shows otherwise than the settings hold them - a
    /// value past the end of its slider, one its list lacks - so a save leaves
    /// them as the file has them until the control is moved.
    /// </summary>
    private void NoteShownValues() =>
        _shownUnlikeSettings = AppServices.Current.SettingsStore.ShownUnlike(Trial());

    private void ForgetShown(IEnumerable<string> keys)
    {
        var forgotten = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        _shownUnlikeSettings = _shownUnlikeSettings
            .Where(pair => !forgotten.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The settings with this screen's edits in, apart from the ones a save writes.</summary>
    private SimulatorSettings Trial()
    {
        var trial = AppServices.Current.Settings.Copy();
        CaptureControls(trial, remember: false);
        return trial;
    }

    /// <summary>
    /// Rows that only matter while another is on: the shutter with motion blur,
    /// and the reflections with their cube map - which only the full renderer can
    /// switch off; the others draw reflections regardless.
    /// </summary>
    private void UpdateDependentRows()
    {
        if (_keyRows.RowOf("gfx.postfx.motionblur.shutter") is { } shutter)
            shutter.IsEnabled = IsChecked(MotionBlurCb);
        ReflectionRows.IsEnabled = IsChecked(ReflectionsCubeMapCb) ||
                                   SelectedRendererKind() != RendererKind.Modern;
    }

    private string SelectedRenderer() =>
        SimulatorSettings.RenderEngines.ElementAtOrDefault(RenderEngineCb.SelectedIndex) ?? "full";

    private RendererKind SelectedRendererKind() => SettingReach.KindOf(SelectedRenderer());

    /// <summary>
    /// Writes the pending edits on this screen into the settings, so a save from
    /// anywhere in the application picks them up.
    /// </summary>
    public void CaptureInto(SimulatorSettings s) => CaptureControls(s, remember: true);

    /// <param name="remember">
    /// Whether the rows of <see cref="SimulatorKeyRows"/> take what they hand over
    /// for what they show - only for the settings a save writes.
    /// </param>
    private void CaptureControls(SimulatorSettings s, bool remember)
    {

        var language = ChangeLanguageCb.SelectedItem as ComboBoxItem;
        s.Language = language?.Content?.ToString() ?? App.Loc.CurrentLanguage;
        s.LanguageCode = language?.Tag as string ?? App.Loc.CurrentLangCode;
        s.Fullscreen = IsChecked(FullscreenCb);
        s.PauseWhenInactive = IsChecked(PauseInactiveCb);
        s.PauseOnStart = IsChecked(PauseStartCb);
        s.CursorSensitivity = (int)CursorSensitivitySlider.Value;
        s.InvertMouseHorizontal = IsChecked(MouseHorInvertCb);
        s.InvertMouseVertical = IsChecked(MouseVertInvertCb);

        s.IgnoreGamepad = IsChecked(GamepadIgnoreCb);
        s.FeedbackMode = Math.Max(0, FeedbackCb.SelectedIndex);
        s.FeedbackPort = (int)(FeedbackPortNud.Value ?? 888);
        CaptureUart(s);

        s.SelectExeAutomatically = SelectExeCb.SelectedIndex == 0;
        s.ExecutablePath = s.SelectExeAutomatically ? "eu07.exe"
            : (SelectExeCb.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "eu07.exe";
        s.DebugMode = IsChecked(DebugModeCb);
        s.VirtualShunting = IsChecked(VirtualShuntingCb);
        s.LogMissingVehicleFiles = IsChecked(LogMissingVehicleFilesCb);

        s.RenderEngine = Math.Max(0, RenderEngineCb.SelectedIndex);
        s.Gpu = SelectedGpu();
        ReadResolution(s);
        s.BufferScalePercent = (int)bufferScale.Value;
        s.MaxTextureSize = 1 << (int)textureResolutionSlider.Value;
        s.MaxCabTextureSize = 1 << (int)cabTextureResolutionSlider.Value;
        s.TextureFiltering = _anisotropy.Value;
        s.Multisampling = (int)_multisampling.Value;
        s.DynamicLights = (int)DynamicLightsSlider.Value;
        s.DrawRangeFactor = _drawRangeFactor.Value;
        s.VSync = IsChecked(VSyncCb);
        s.Smoke = IsChecked(SmokeDisplayCb);
        s.SmokeFidelity = (int)SmokeParticlesSlider.Value;
        s.ChromaticAberration = IsChecked(ChromaticAberrationCb);
        s.MotionBlur = IsChecked(MotionBlurCb);
        s.ExtraEffects = IsChecked(AdditionalShadersCb);
        s.EnvMap = IsChecked(ReflectionsCubeMapCb);
        s.UseVbo = IsChecked(RenderVBOCb);
        s.RenderShadows = IsChecked(RenderShadowsCb);
        s.ReflectionsFramerate = _reflections.Value;
        s.ShadowMapResolution = 1 << (int)shaderResolutionSlider.Value;
        s.ShadowProjectionRange = (int)shaderRange.Value;
        s.CabShadowsRange = (int)cabShaderSourceRange.Value;
        s.ShadowRankCutoff = Math.Max(0, ShadowDisplayCb.SelectedIndex) + 1;
        s.ReflectionsFidelity = Math.Max(0, ReflectionsDetailsCb.SelectedIndex);
        s.FieldOfView = Math.Clamp((int)fovSlider.Value, 10, 75);
        s.PythonScreens = IsChecked(RenderScreensCb);
        s.PythonThreadedUpload = IsChecked(RenderScreensThreadCb);
        // The first step is no limit; the last limit set is kept for the next time.
        double fpsLimit = _fpsLimit.Value;
        s.FpsLimitEnabled = fpsLimit > 0;
        if (fpsLimit > 0)
            s.FpsLimit = fpsLimit;
        s.ShadowAngleLimit = ShadowAngleLimitSlider.Value;
        s.FullscreenWindowed = IsChecked(FullscreenWindowedCb);
        s.RenderAngleVulkan = IsChecked(RenderAngleVulkanCb);

        s.SplineFidelity = (int)TrackCurvesSlider.Value;
        s.FullPhysics = IsChecked(PhysicsAccuracyCb);
        s.EnableTraction = IsChecked(PantographBreakCb);
        s.LiveTraction = IsChecked(OverheadOnlyCb);
        s.PhysicsLog = IsChecked(SpeedometerTapesCb);
        s.DebugLog = IsChecked(SimLogsCb);
        s.MultipleLogs = IsChecked(KeepLogsCb);
        s.DisplaySimulation = IsChecked(DisplaySimulationCb);
        s.CrashDamage = IsChecked(CrashDamageCb);
        s.Friction = FrictionSlider.Value;
        s.BrakeStep = BrakeStepSlider.Value;
        s.BrakeSpeed = BrakeSpeedSlider.Value;

        s.SoundEnabled = IsChecked(EnableSoundsCb);
        s.Volume = (int)VolumeSlider.Value;
        s.RadioVolume = (int)RadioVolumeSlider.Value;
        s.VehiclesVolume = (int)VehiclesVolumeSlider.Value;
        s.PositionalVolume = (int)PositionalVolumeSlider.Value;
        s.AmbientVolume = (int)AmbientVolumeSlider.Value;
        s.SkipPipeline = IsChecked(SkipPipelineCb);
        s.DebugLogVisible = IsChecked(DebugLogVisibleCb);
        s.PythonScreenUpdateRate = ParsePriorityTag((PyScreenPriorityCb.SelectedItem as ComboBoxItem)?.Tag);

        s.AutoCloseStarter = IsChecked(AutoCloseStarterCb);
        s.LargeThumbnails = IsChecked(LargeThumbnailsCb);
        s.AutoExpandSceneryTree = IsChecked(AutoExpandTreeCb);
        s.ReadyOverride = (ConsistReadyOverride)Math.Clamp(ReadyOverrideCb.SelectedIndex, 0, 2);

        s.CompressTextures = IsChecked(CompressTexCb);
        s.ScaleSpeculars = IsChecked(ScaleSpecularsCb);
        s.UseGLES = IsChecked(UseGlesCb);
        s.ShaderGamma = IsChecked(ShaderGammaCb);
        s.ScreenMipmaps = IsChecked(ScreenMipmapsCb);
        s.ExtendedModelConversion = IsChecked(ConvertModelsCb);
        s.GfxResourceMove = IsChecked(ResourceMoveCb);
        s.GfxResourceSweep = IsChecked(ResourceSweepCb);
        s.TrainPhysicsThreads = (int)TrainThreadsSlider.Value;
        s.IgnoreIrrelevantTrains = IsChecked(IgnoreIrrelevantCb);

        _keyRows.Capture(s, remember);
    }

    // ── UART ─────────────────────────────────────────────────────────────────

    // The interval as last shown: the box cannot hold every value a file may have, and one
    // nobody moved stays as it was.
    private decimal _uartEveryShown;

    private void HookUart()
    {
        // A board plugged in after the screen opened shows up on the next look.
        UartPortCb.DropDownOpened += (_, _) => FillUartPorts(UartPortCb.Text);
        UartPortCb.PropertyChanged += (_, e) =>
        {
            if (e.Property != ComboBox.TextProperty)
                return;
            ValidateUart();
            OnControlChanged();
        };
        UartEnabledCb.IsCheckedChanged += (_, _) => ValidateUart();
        UartTuneBox.TextChanged += (_, _) =>
        {
            ValidateUart();
            OnControlChanged();
        };
        App.Loc.LanguageChanged += UpdateUartTexts;
        UpdateUartTexts();
    }

    private void UpdateUartTexts() =>
        UartTuneWarning.Text = string.Format(App.Loc["UartTuneInvalid"], Uart.TuneValues);

    private void FillUartPorts(string? current)
    {
        var ports = SerialPorts.List().ToList();
        if (!string.IsNullOrWhiteSpace(current) && !ports.Contains(current))
            ports.Insert(0, current);
        UartPortCb.ItemsSource = ports;
        UartPortCb.Text = current;
    }

    private void ValidateUart()
    {
        UartPortWarning.IsVisible = IsChecked(UartEnabledCb) && !Uart.IsPortName(UartPortCb.Text);
        UartTuneWarning.IsVisible = !string.IsNullOrWhiteSpace(UartTuneBox.Text) && Uart.NormalizeTune(UartTuneBox.Text) is null;
    }

    private void ShowUart(SimulatorSettings s)
    {
        UartEnabledCb.IsChecked = s.UartEnabled;
        FillUartPorts(s.UartPort);

        var rates = new List<int>(Uart.BaudRates);
        // One the simulator's list lacks still works; keep it rather than lose it.
        if (!rates.Contains(s.UartBaud))
            rates.Add(s.UartBaud);
        rates.Sort();
        UartBaudCb.ItemsSource = rates.Select(r => r.ToString(CultureInfo.InvariantCulture)).ToList();
        UartBaudCb.SelectedIndex = rates.IndexOf(s.UartBaud);

        _uartEveryShown = Uart.ShownUpdateTime(s.UartUpdateTime);
        UartEveryNud.Maximum = (decimal)Uart.MaxShownUpdateTime;
        UartEveryNud.Value = _uartEveryShown;
        UartTuneBox.Text = s.UartTune;
        UartDebugCb.IsChecked = s.UartDebug;
        UartMainCb.IsChecked = s.UartMain;
        UartScndCb.IsChecked = s.UartScnd;
        UartTrainCb.IsChecked = s.UartTrain;
        UartLocalCb.IsChecked = s.UartLocal;
        UartRadioVolumeCb.IsChecked = s.UartRadioVolume;
        UartRadioChannelCb.IsChecked = s.UartRadioChannel;
        ValidateUart();
    }

    private void CaptureUart(SimulatorSettings s)
    {
        s.UartEnabled = IsChecked(UartEnabledCb);
        s.UartPort = (UartPortCb.Text ?? string.Empty).Trim();
        if (int.TryParse(UartBaudCb.SelectedItem as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out int baud))
            s.UartBaud = baud;
        if (UartEveryNud.Value is { } every && every != _uartEveryShown)
            s.UartUpdateTime = (double)every;
        s.UartTune = UartTuneBox.Text ?? string.Empty;
        s.UartDebug = IsChecked(UartDebugCb);
        s.UartMain = IsChecked(UartMainCb);
        s.UartScnd = IsChecked(UartScndCb);
        s.UartTrain = IsChecked(UartTrainCb);
        s.UartLocal = IsChecked(UartLocalCb);
        s.UartRadioVolume = IsChecked(UartRadioVolumeCb);
        s.UartRadioChannel = IsChecked(UartRadioChannelCb);
    }

    private void OpenLogToolsButton_OnClick(object? sender, RoutedEventArgs e) =>
        (TopLevel.GetTopLevel(this) as MainWindow)?.OpenLogAndTools();

    private void SaveButton_OnClick(object? sender, RoutedEventArgs e) => Save();

    /// <summary>Shows what a save would change in eu07.ini, and saves if asked to.</summary>
    private async void ChangesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var store = AppServices.Current.SettingsStore;
        var changes = store.Changes(Trial());
        if (await IniChangesWindow.Show(owner, store.SavePath, changes, KeyboardConfig.Instance.Dirty))
            Save();
    }

    public void Save()
    {
        CaptureInto(AppServices.Current.Settings);
        var store = AppServices.Current.SettingsStore;
        store.Save();
        KeyboardConfig.Instance.Save();
        RedrawThumbnailsIfNeeded();
        // Not saved, it is still there to be saved.
        _dirty = store.LastSaveFailed;
        UpdateSaveState();
        SaveStatus.Text = App.Loc[store.LastSaveFailed ? "SettingsNotSaved" : "SettingsSaved"];
    }

    private void RenderEngineCb_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_built)
            return;

        RefreshDerivedRows();
    }

    // ── The graphics card of a native Linux simulator ────────────────────────

    private IReadOnlyList<GpuDevice>? _gpus;

    private void GpuCb_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_built)
            return;

        UpdateGpuNote();
    }

    /// <summary>Names the cards found and says which one the simulator would get with the choice made here.</summary>
    private void UpdateGpuNote()
    {
        if (!AppServices.Current.Environment.IsLinux)
            return;

        _gpus ??= AppServices.Current.Gpus.List();
        var environment = AppServices.Current.Environment;
        var preference = SelectedGpu();
        string cards = string.Join(", ", _gpus);

        GpuRow.Description = GpuOffload.NoteFor(_gpus, preference, environment.GetVariable) switch
        {
            GpuNote.Unread => App.Loc["GpuNoteUnread"],
            GpuNote.OneCard => string.Format(App.Loc["GpuNoteOne"], cards),
            GpuNote.LeftToSystem => string.Format(App.Loc["GpuNoteSystem"], cards),
            GpuNote.PlayerChose => string.Format(App.Loc["GpuNotePlayer"], cards,
                                                 string.Join(", ", GpuOffload.PlayerChoices(environment.GetVariable))),
            GpuNote.Moved => string.Format(App.Loc["GpuNoteTarget"], cards,
                                           GpuOffload.Target(_gpus, preference, environment.GetVariable)),
            _ => string.Format(App.Loc["GpuNoteNone"], cards)
        };
    }

    private GpuPreference SelectedGpu() => GpuCb.SelectedIndex == 1 ? GpuPreference.System : GpuPreference.Discrete;

    // ── Settings the chosen simulator would pass over ────────────────────────

    /// <summary>The simulator the start button would run with the pick on this screen.</summary>
    private string TargetExecutable()
    {
        var exes = AppServices.Current.Executables;
        var pick = new SimulatorSettings
        {
            SelectExeAutomatically = SelectExeCb.SelectedIndex <= 0,
            ExecutablePath = (SelectExeCb.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty
        };
        return exes.FullPath(exes.Resolve(pick, out _));
    }

    /// <summary>
    /// Dims the rows the simulator to be started would ignore and says why. Follows
    /// the simulator and renderer picked here, before they are saved.
    /// </summary>
    private void UpdateIdleNotes()
    {
        string exe = TargetExecutable();
        var settings = AppServices.Current.Settings;
        var target = new SimulatorTarget(
            AppServices.Current.Executables.IsWindowsBuild(exe),
            SelectedRenderer(),
            settings.NetworkServer,
            _keyRows.NumberIfSet("targetfps", settings),
            settings.FpsAverage,
            IsChecked(FullscreenWindowedCb));

        foreach (var row in SettingsStack.GetLogicalDescendants().OfType<SettingRow>())
            row.IdleNote = string.IsNullOrEmpty(row.ConfigKey)
                ? null
                : IdleText(SettingReach.Why(row.ConfigKey, target));

        RenderEngineWarning.IsVisible = SelectedRendererKind() == RendererKind.Experimental;
    }

    private static string? IdleText(IdleReason reason) => reason switch
    {
        IdleReason.WindowsBuildOnly => App.Loc["IdleWindowsBuildOnly"],
        IdleReason.LegacyRendererOnly => App.Loc["IdleLegacyRendererOnly"],
        IdleReason.ModernRendererOnly => App.Loc["IdleModernRendererOnly"],
        IdleReason.OpenGlRenderersOnly => App.Loc["IdleOpenGlRenderersOnly"],
        IdleReason.NeedsNetworkServer => App.Loc["IdleNeedsNetworkServer"],
        IdleReason.NeedsFrameRateTarget => App.Loc["IdleNeedsFrameRateTarget"],
        IdleReason.MonitorModeInstead => App.Loc["IdleMonitorMode"],
        _ => null
    };

    private void RedrawThumbnailsIfNeeded()
    {
        bool large = AppServices.Current.Settings.LargeThumbnails;
        if (large == _drawnThumbs)
            return;

        _drawnThumbs = large;
        ThumbnailSizeChanged?.Invoke();
    }

    private void ResetButton_OnClick(object? sender, RoutedEventArgs e)
    {
        AppServices.Current.SettingsStore.Load();
        ApplyToUi();
        RedrawThumbnailsIfNeeded();
        _keyBindings.Reload();
        _dirty = false;
        UpdateSaveState();
    }

    /// <summary>The list as <see cref="ResolutionChoices"/> has it for the monitors there are now, with the size selected.</summary>
    private void SelectResolution(int width, int height)
    {
        var wanted = new ScreenSize(width, height);
        var shown = ShownResolution();
        var monitors = TopLevel.GetTopLevel(this)?.Screens is { } screens
            ? screens.All.OrderByDescending(m => m.IsPrimary)
                .Select(m => new ScreenSize(m.Bounds.Width, m.Bounds.Height)).ToList()
            : new List<ScreenSize>();

        // Filling the list is not an edit; a size other than the one shown is.
        WhileLoading(() =>
        {
            ResolutionCb.Items.Clear();
            foreach (var choice in ResolutionChoices.For(monitors, wanted))
            {
                var item = new ComboBoxItem
                {
                    Tag = choice.Size,
                    Content = choice.IsMonitor
                        ? string.Format(App.Loc["ResolutionOnMonitor"], choice.Size)
                        : choice.Size.ToString()
                };
                ResolutionCb.Items.Add(item);
                if (choice.Size == wanted)
                    ResolutionCb.SelectedItem = item;
            }
        });

        if (shown != wanted)
            MarkDirty();
    }

    private ScreenSize? ShownResolution() => (ResolutionCb.SelectedItem as ComboBoxItem)?.Tag as ScreenSize?;

    /// <summary>A monitor came or went, or the language changed: the same size, on a list made afresh.</summary>
    private void RefreshResolutions()
    {
        if (ShownResolution() is { } shown)
            SelectResolution(shown.Width, shown.Height);
    }

    private void ReadResolution(SimulatorSettings s)
    {
        if (ShownResolution() is { } size)
        {
            s.Width = size.Width;
            s.Height = size.Height;
        }
    }

    private void TextureResolutionSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_built)
            return;

        ShowPixels(TexResolutionRow, textureResolutionSlider);
    }

    private void CabTextureResolutionSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_built)
            return;

        ShowPixels(CabTexResolutionRow, cabTextureResolutionSlider);
    }

    private void shaderResolutionSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_built)
            return;

        UpdateShadowResolution();
    }

    /// <summary>A power of two as the row of its slider says it; the size itself for what else follows it.</summary>
    private static int ShowPixels(SettingRow row, Slider slider)
    {
        int resolution = 1 << (int)slider.Value;
        row.ValueText = $"{resolution} px";
        return resolution;
    }

    private void UpdateShadowResolution()
    {
        int resolution = ShowPixels(ShadowResolutionRow, shaderResolutionSlider);
        ToolTip.SetTip(shaderResolutionSlider, SettingTexts.ShadowMapWord(resolution));

        // Below this the simulator would raise the range anyway.
        shaderRange.Minimum = SimulatorSettings.ShadowRangeFloor(resolution);
        UpdateQualityCaptions();
    }

    private void FillLanguageCombo()
    {
        ChangeLanguageCb.Items.Clear();
        foreach (var lang in App.Loc.Languages())
            ChangeLanguageCb.Items.Add(new ComboBoxItem { Content = lang.Name, Tag = lang.Code });
        if (ChangeLanguageCb.ItemCount == 0)
            ChangeLanguageCb.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });
        SelectActiveLanguage();
    }

    private void SelectActiveLanguage()
    {
        var item = ChangeLanguageCb.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(it => string.Equals(it.Tag as string, App.Loc.CurrentLangCode,
                                                StringComparison.OrdinalIgnoreCase))
            ?? ChangeLanguageCb.Items.OfType<ComboBoxItem>().FirstOrDefault();

        if (item != null && !ReferenceEquals(ChangeLanguageCb.SelectedItem, item))
            ChangeLanguageCb.SelectedItem = item;

        LanguageRow.Description = !App.Loc.HasOwnTexts
            ? App.Loc["LanguageStarterMissing"]
            : SimulatorSpeaks(App.Loc.CurrentLangCode)
                ? App.Loc.Opt("LanguageDesc")
                : App.Loc["LanguageSimMissing"];
    }

    /// <summary>The simulator reads lang/&lt;code&gt;.po and is in English without one.</summary>
    private static bool SimulatorSpeaks(string code) =>
        string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ||
        AppServices.Current.Files.FileExists(AppServices.Current.Paths.FromRoot("lang", code + ".po"));

    private void LanguageComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if ((ChangeLanguageCb.SelectedItem as ComboBoxItem)?.Tag is string code)
            App.ChangeLanguage(code);
    }

    private void FeedbackCb_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        UpdateFeedbackDetailVisibility();

    /// <summary>
    /// The port and UART rows only apply to some feedback modes. Visibility is
    /// decided in one place — <see cref="ApplyFilter"/> — so that a search cannot
    /// reveal a row the selected mode does not use.
    /// </summary>
    private void UpdateFeedbackDetailVisibility() => ApplyFilter();

    private bool ConditionAllows(SettingRow row)
    {
        if (ReferenceEquals(row, GpuRow))
            return AppServices.Current.Environment.IsLinux;
        if (ReferenceEquals(row, FeedbackPortRow))
            return FeedbackCb.SelectedIndex == 3;
        if (ReferenceEquals(row.FindLogicalAncestorOfType<SettingsSection>(), UartSettings))
            return FeedbackCb.SelectedIndex == 5;
        return true;
    }

    // ── Category navigation, search and unsaved-changes state ────────────────

    private bool _dirty;

    private void CategoryList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_built)
            return;

        // Picking a category is how you leave a search, so the query goes with it.
        if (SettingsSearch is { Text.Length: > 0 })
            SettingsSearch.Text = string.Empty;   // raises TextChanged -> ApplyFilter
        else
            ApplyFilter();
    }

    private void SettingsSearch_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_built)
            return;

        ApplyFilter();
    }

    /// <summary>
    /// Single owner of what is on screen. With an empty query it shows the selected
    /// category; with a query it stacks every category and keeps only the rows that
    /// match, collapsing sections and categories that end up empty.
    /// </summary>
    private void ApplyFilter()
    {
        string query = SettingsSearch.Text?.Trim() ?? string.Empty;
        bool searching = query.Length > 0;
        string category = (CategoryList.SelectedItem as ListBoxItem)?.Tag as string ?? string.Empty;

        // Key bindings are their own world: they bring their own scrolling and a
        // star-sized row, so they replace the settings scroller rather than living
        // in it, and the search box filters them instead of the settings.
        bool bindings = category == "controls";
        ControlsHost.IsVisible = bindings;
        SettingsScroll.IsVisible = !bindings;
        PresetBar.IsVisible = category == "graphics" && !searching;

        if (bindings)
        {
            _keyBindings.RebuildList();
            foreach (var entry in CategoryList.Items.OfType<ListBoxItem>())
                entry.Classes.Set("NoHits", false);
            return;
        }

        foreach (var panel in SettingsStack.Children.OfType<StackPanel>())
        {
            bool anyRow = false;

            foreach (var section in panel.GetLogicalDescendants().OfType<SettingsSection>())
            {
                bool anyInSection = false;

                foreach (var row in section.GetLogicalDescendants().OfType<SettingRow>())
                {
                    bool visible = ConditionAllows(row) &&
                                   (!searching || TextMatch.Contains(RowSearchText(row), query));
                    row.IsVisible = visible;
                    anyInSection |= visible;
                }

                section.IsVisible = anyInSection;
                anyRow |= anyInSection;
            }

            string tag = panel.Tag as string ?? string.Empty;
            panel.IsVisible = anyRow && (searching || tag == category);

            // The caption only earns its place when several categories are stacked.
            foreach (var caption in panel.Children.OfType<TextBlock>())
                caption.IsVisible = searching;

            if (CategoryItem(tag) is { } item)
                item.Classes.Set("NoHits", searching && !anyRow);
        }

        if (searching)
            SettingsScroll.Offset = new Vector(0, 0);
    }

    private ListBoxItem? CategoryItem(string tag) =>
        CategoryList.Items.OfType<ListBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.Ordinal));

    /// <summary>
    /// Everything about a row a user might type: its label, its explanation and
    /// the text of the control itself (a checkbox caption, a dropdown's options).
    /// </summary>
    private static string RowSearchText(SettingRow row)
    {
        var sb = new StringBuilder();
        sb.Append(row.Label).Append(' ').Append(row.Description).Append(' ');

        switch (row.Content)
        {
            case CheckBox cb:
                sb.Append(cb.Content);
                break;
            case Button btn:
                sb.Append(btn.Content);
                break;
            case ComboBox combo:
                foreach (var item in combo.Items.OfType<ComboBoxItem>())
                    sb.Append(item.Content).Append(' ');
                break;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Subscribes every input in the panel to <see cref="MarkDirty"/> once, so the
    /// Save button can tell the user there is something to save. Section master
    /// switches hang off SettingsSection.Toggle rather than the logical tree, so
    /// they are collected separately.
    /// </summary>
    private void HookDirtyTracking()
    {
        foreach (var control in this.GetLogicalDescendants().OfType<Control>())
        {
            switch (control)
            {
                // Choosing a profile changes nothing until Apply is pressed, and a
                // language is saved the moment it is picked.
                case ComboBox combo when ReferenceEquals(combo, ProfileCb) ||
                                         ReferenceEquals(combo, ChangeLanguageCb):
                    break;
                // The rows of SimulatorKeyRows report their own changes.
                case { Tag: SimulatorKey }:
                    break;
                case CheckBox cb:
                    cb.IsCheckedChanged += (_, _) => OnControlChanged();
                    break;
                case Slider slider:
                    slider.ValueChanged += (_, _) => OnControlChanged();
                    break;
                case ComboBox combo:
                    combo.SelectionChanged += (_, _) => OnControlChanged();
                    break;
                case NumericUpDown nud:
                    nud.ValueChanged += (_, _) => OnControlChanged();
                    break;
            }
        }

        foreach (var section in this.GetLogicalDescendants().OfType<SettingsSection>())
            if (section.Toggle is CheckBox toggle)
                toggle.IsCheckedChanged += (_, _) => OnControlChanged();
    }

    private void OnControlChanged()
    {
        MarkDirty();
        QueuePresetMarks();
    }

    private void MarkDirty()
    {
        if (_loading || _dirty)
            return;

        _dirty = true;
        UpdateSaveState();
    }

    private void UpdateSaveState()
    {
        bool dirty = _dirty || KeyboardConfig.Instance.Dirty;
        SaveButton.Classes.Set("Accent", dirty);
        SaveStatus.Text = dirty ? App.Loc["UnsavedChanges"] : string.Empty;
    }

    private static bool IsChecked(CheckBox cb) => cb.IsChecked == true;

    private static int ParsePriorityTag(object? tag)
    {
        if (tag is int i) return i;
        if (tag is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return n;
        return 200;
    }

    private static int Log2(int value, int fallback)
    {
        if (value <= 0) return fallback;
        int log = (int)Math.Round(Math.Log2(value));
        return log;
    }

    private void QualitySlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_built)
            return;

        UpdateQualityCaptions();
    }

    private void UpdateQualityCaptions()
    {
        var q = SettingTexts.QualityWords;

        TexFilteringRow.ValueText = SettingTexts.Anisotropy(_anisotropy.Value);

        TrackCurvesRow.ValueText = SettingTexts.Fidelity((int)TrackCurvesSlider.Value);
        SmokeParticlesRow.ValueText = SettingTexts.Fidelity((int)SmokeParticlesSlider.Value);

        MultisamplingRow.ValueText = SettingTexts.Multisampling((int)_multisampling.Value);

        RenderRangeRow.ValueText = "×" + _drawRangeFactor.Value.ToString("0.0", CultureInfo.CurrentCulture);

        CursorSensitivityRow.ValueText = (int)CursorSensitivitySlider.Value switch
        {
            <= 1 => q[1],
            2 => App.Loc["QualityStandard"],
            3 => q[3],
            _ => q[4]
        };

        int metres = (int)shaderRange.Value;
        // Only the full renderer's cascades are known to stretch.
        int reach = SimulatorSettings.ShadowReach(metres, 1 << (int)shaderResolutionSlider.Value,
            SelectedRendererKind() != RendererKind.Modern);
        ShadowRangeRow.ValueText = reach == metres ? $"{metres} m" : $"{metres} m → {reach} m";
        ToolTip.SetTip(shaderRange, SettingTexts.ShadowRangeWord(reach));

        int cabMetres = (int)cabShaderSourceRange.Value;
        CabShadowRangeRow.ValueText = cabMetres <= 0 ? App.Loc["RangeDisabled"] : $"{cabMetres} m";
        ToolTip.SetTip(cabShaderSourceRange, SettingTexts.CabShadowRangeWord(cabMetres));

        ReflectionsFramerateRow.ValueText = SettingTexts.ReflectionsInterval(_reflections.Value);

        FpsLimitRow.ValueText = _fpsLimit.Value <= 0
            ? App.Loc["FpsLimitOff"]
            : _fpsLimit.Value.ToString("0.##", CultureInfo.CurrentCulture) + " FPS";

        // Set here rather than bound with a StringFormat, which would keep the old
        // language's decimal separator until the slider moved.
        ShadowAngleLimitRow.ValueText = SettingTexts.TwoDecimals(ShadowAngleLimitSlider.Value);
        FrictionRow.ValueText = SettingTexts.TwoDecimals(FrictionSlider.Value);
        BrakeStepRow.ValueText = SettingTexts.TwoDecimals(BrakeStepSlider.Value);
        BrakeSpeedRow.ValueText = SettingTexts.TwoDecimals(BrakeSpeedSlider.Value);
        TrainThreadsRow.ValueText = ((int)TrainThreadsSlider.Value).ToString(CultureInfo.CurrentCulture);
    }
}
