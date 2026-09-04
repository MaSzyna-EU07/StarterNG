using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using StarterNG.Classes;
using StarterNG.Controls;
using StarterNG.Infrastructure;
using StarterNG.Services;

using KeyBinding = StarterNG.Classes.KeyBinding;
using StarterNG.Domain.Settings;
using StarterNG.Application;

namespace StarterNG.Views;

public partial class Settings : UserControl, ISettingsCapture
{
    private bool _loading;

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

        SettingsPathText.Text = AppServices.Current.SettingsStore.LoadedFrom;
        ToolTip.SetTip(SettingsPathText, AppServices.Current.SettingsStore.LoadedFrom);

        this.AttachedToVisualTree += (_, _) =>
        {
            TextureResolutionSlider_OnValueChanged(null, null);
            CabTextureResolutionSlider_OnValueChanged(null, null);
            shaderResolutionSlider_OnValueChanged(null, null);
            UpdateQualityCaptions();
        };

        FillLanguageCombo();

        App.Loc.LanguageChanged += SelectActiveLanguage;
        App.Loc.LanguageChanged += UpdateQualityCaptions;
        App.Loc.LanguageChanged += UpdateSaveState;
        App.Loc.LanguageChanged += () => shaderResolutionSlider_OnValueChanged(null, null);

        FeedbackCb.SelectionChanged += FeedbackCb_OnSelectionChanged;
        FpsLimitEnableCb.IsCheckedChanged += (_, _) => FpsLimitSlider.IsEnabled = IsChecked(FpsLimitEnableCb);

        AppServices.Current.SettingsStore.RegisterCapture(this);

        PopulateProfileCombo();
        ApplyToUi();

        KeyboardConfig.Instance.Load();
        BuildControlsTab();
        AddHandler(KeyDownEvent, OnControlsKeyDown, RoutingStrategies.Tunnel);

        HookDirtyTracking();
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
    }

    private void ProfileApplyButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ProfileCb.SelectedItem is not ComboBoxItem { Content: string name } || string.IsNullOrWhiteSpace(name))
            return;
        string path = SettingsProfileStore.PathFor(name);
        if (!File.Exists(path)) return;
        AppServices.Current.SettingsStore.LoadFrom(path);
        ApplyToUi();
        RedrawThumbnailsIfNeeded();
    }

    private void ProfileSaveAsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = App.Loc["ProfileNamePrompt"], MinWidth = 220 };
        var ok = new Button { Content = App.Loc["ProfileSaveAs"] };
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(8), MinWidth = 240 };
        panel.Children.Add(new TextBlock { Text = App.Loc["ProfileNamePrompt"], FontWeight = FontWeight.Bold, FontSize = 12 });
        panel.Children.Add(nameBox);
        panel.Children.Add(ok);
        var flyout = new Flyout { Content = panel };

        void Commit()
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text)) return;
            CaptureInto(AppServices.Current.Settings);
            AppServices.Current.SettingsStore.SaveTo(SettingsProfileStore.PathFor(nameBox.Text));
            PopulateProfileCombo();
            int idx = FindComboIndexByContent(ProfileCb, nameBox.Text.Trim());
            if (idx >= 0) ProfileCb.SelectedIndex = idx;
            flyout.Hide();
        }
        ok.Click += (_, _) => Commit();
        nameBox.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) { ev.Handled = true; Commit(); } };
        flyout.ShowAt(ProfileSaveAsBtn, showAtPointer: false);
        nameBox.Focus(); nameBox.SelectAll();
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

    public void ReloadFromSettings() => ApplyToUi();

    private void ApplyToUi()
    {
        var s = AppServices.Current.Settings;
        _loading = true;
        try
        {
            PopulateProfileCombo();
            PopulateExeCombo();

            SelectActiveLanguage();
            FullscreenCb.IsChecked = s.Fullscreen;
            PauseInactiveCb.IsChecked = s.PauseWhenInactive;
            PauseStartCb.IsChecked = s.PauseOnStart;
            CursorSensitivitySlider.Value = s.CursorSensitivity;
            MouseHorInvertCb.IsChecked = s.InvertMouseHorizontal;
            MouseVertInvertCb.IsChecked = s.InvertMouseVertical;

            GamepadIgnoreCb.IsChecked = s.IgnoreGamepad;
            FeedbackCb.SelectedIndex = s.FeedbackMode;
            FeedbackPortNud.Value = (decimal)s.FeedbackPort;
            UpdateFeedbackDetailVisibility();

            if (s.SelectExeAutomatically)
            {
                SelectExeCb.SelectedIndex = 0;
            }
            else
            {
                int exeIdx = FindComboIndexByContent(SelectExeCb, s.ExecutablePath);
                if (exeIdx < 0)
                {
                    SelectExeCb.Items.Add(new ComboBoxItem { Content = s.ExecutablePath });
                    exeIdx = SelectExeCb.Items.Count - 1;
                }
                SelectExeCb.SelectedIndex = exeIdx;
            }
            DebugModeCb.IsChecked = s.DebugMode;
            VirtualShuntingCb.IsChecked = s.VirtualShunting;
            LogMissingVehicleFilesCb.IsChecked = s.LogMissingVehicleFiles;

            RenderEngineCb.SelectedIndex = s.RenderEngine;
            SelectResolution(s.Width, s.Height);
            bufferScale.Value = s.BufferScalePercent;
            textureResolutionSlider.Value = Log2(s.MaxTextureSize, 12);
            cabTextureResolutionSlider.Value = Log2(s.MaxCabTextureSize, 12);
            TexFilteringSlider.Value = AnisotropyToSlider(s.TextureFiltering);
            MultisamplingSlider.Value = s.Multisampling + 1;
            DynamicLightsSlider.Value = s.DynamicLights;
            RenderRangeSlider.Value = (int)Math.Round(s.DrawRangeFactor);
            VSyncCb.IsChecked = s.VSync;
            SmokeDisplayCb.IsChecked = s.Smoke;
            SmokeParticlesSlider.Value = s.SmokeFidelity;
            ChromaticAberrationCb.IsChecked = s.ChromaticAberration;
            MotionBlurCb.IsChecked = s.MotionBlur;
            AdditionalShadersCb.IsChecked = s.ExtraEffects;
            ReflectionsCubeMapCb.IsChecked = s.EnvMap;
            RenderVBOCb.IsChecked = s.UseVbo;
            RenderShadowsCb.IsChecked = s.RenderShadows;
            reflectionsFramerate.Value = s.ReflectionsFramerate;
            shaderResolutionSlider.Value = Log2(s.ShadowMapResolution, 12);
            shaderRange.Value = s.ShadowProjectionRange;
            cabShaderSourceRange.Value = s.CabShadowsRange;
            ShadowDisplayCb.SelectedIndex = Clamp(s.ShadowRankCutoff - 1, 0, 2);
            ReflectionsDetailsCb.SelectedIndex = s.ReflectionsFidelity;
            fovSlider.Value = s.FieldOfView;
            RenderScreensCb.IsChecked = s.PythonScreens;
            RenderScreensThreadCb.IsChecked = s.PythonThreadedUpload;
            FpsLimitEnableCb.IsChecked = s.FpsLimitEnabled;
            FpsLimitSlider.Value = s.FpsLimit;
            FpsLimitSlider.IsEnabled = s.FpsLimitEnabled;
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
        }
        finally
        {
            _loading = false;
        }

        UpdateQualityCaptions();
    }

    /// <summary>
    /// Writes the pending edits on this screen into the settings, so a save from
    /// anywhere in the application picks them up.
    /// </summary>
    public void CaptureInto(SimulatorSettings s)
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

        s.SelectExeAutomatically = SelectExeCb.SelectedIndex == 0;
        s.ExecutablePath = s.SelectExeAutomatically ? "eu07.exe"
            : (SelectExeCb.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "eu07.exe";
        s.DebugMode = IsChecked(DebugModeCb);
        s.VirtualShunting = IsChecked(VirtualShuntingCb);
        s.LogMissingVehicleFiles = IsChecked(LogMissingVehicleFilesCb);

        s.RenderEngine = Math.Max(0, RenderEngineCb.SelectedIndex);
        ReadResolution(s);
        s.BufferScalePercent = (int)bufferScale.Value;
        s.MaxTextureSize = 1 << (int)textureResolutionSlider.Value;
        s.MaxCabTextureSize = 1 << (int)cabTextureResolutionSlider.Value;
        s.TextureFiltering = SimulatorSettings.AnisotropySteps[
            Clamp((int)TexFilteringSlider.Value - 1, 0, SimulatorSettings.AnisotropySteps.Length - 1)];
        s.Multisampling = Clamp((int)MultisamplingSlider.Value - 1, 0, 3);
        s.DynamicLights = (int)DynamicLightsSlider.Value;
        s.DrawRangeFactor = RenderRangeSlider.Value;
        s.VSync = IsChecked(VSyncCb);
        s.Smoke = IsChecked(SmokeDisplayCb);
        s.SmokeFidelity = (int)SmokeParticlesSlider.Value;
        s.ChromaticAberration = IsChecked(ChromaticAberrationCb);
        s.MotionBlur = IsChecked(MotionBlurCb);
        s.ExtraEffects = IsChecked(AdditionalShadersCb);
        s.EnvMap = IsChecked(ReflectionsCubeMapCb);
        s.UseVbo = IsChecked(RenderVBOCb);
        s.RenderShadows = IsChecked(RenderShadowsCb);
        s.ReflectionsFramerate = (int)reflectionsFramerate.Value;
        s.ShadowMapResolution = 1 << (int)shaderResolutionSlider.Value;
        s.ShadowProjectionRange = (int)shaderRange.Value;
        s.CabShadowsRange = (int)cabShaderSourceRange.Value;
        s.ShadowRankCutoff = Math.Max(0, ShadowDisplayCb.SelectedIndex) + 1;
        s.ReflectionsFidelity = Math.Max(0, ReflectionsDetailsCb.SelectedIndex);
        s.FieldOfView = Clamp((int)fovSlider.Value, 15, 75);
        s.PythonScreens = IsChecked(RenderScreensCb);
        s.PythonThreadedUpload = IsChecked(RenderScreensThreadCb);
        s.FpsLimitEnabled = IsChecked(FpsLimitEnableCb);
        s.FpsLimit = (int)FpsLimitSlider.Value;
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
    }

    private void ComButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
            new UartWindow().ShowDialog(owner);
    }

    private void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        CaptureInto(AppServices.Current.Settings);
        AppServices.Current.SettingsStore.Save();
        KeyboardConfig.Instance.Save();
        RedrawThumbnailsIfNeeded();
        _dirty = false;
        UpdateSaveState();
        if (SaveStatus is not null)
            SaveStatus.Text = App.Loc["SettingsSaved"];
    }

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
        CancelCapture();
        KeyboardConfig.Instance.Load();
        RebuildBindingList();
        RebuildKeyboard();
        _dirty = false;
        UpdateSaveState();
    }

    private void SelectResolution(int width, int height)
    {
        string target = $"{width}x{height}";
        foreach (var obj in ResolutionCb.Items)
        {
            if (obj is ComboBoxItem item && item.Content?.ToString() == target)
            {
                ResolutionCb.SelectedItem = item;
                return;
            }
        }

        var added = new ComboBoxItem { Content = target };
        ResolutionCb.Items.Add(added);
        ResolutionCb.SelectedItem = added;
    }

    private void ReadResolution(SimulatorSettings s)
    {
        var text = (ResolutionCb.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (string.IsNullOrEmpty(text))
            return;
        var parts = text.Split('x');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
        {
            s.Width = w;
            s.Height = h;
        }
    }

    private void TextureResolutionSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (TexResolutionRow is null || textureResolutionSlider is null)
            return;

        int resolution = 1 << (int)textureResolutionSlider.Value;
        TexResolutionRow.ValueText = $"{resolution} px";
    }

    private void CabTextureResolutionSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (CabTexResolutionRow is null || cabTextureResolutionSlider is null)
            return;

        int resolution = 1 << (int)cabTextureResolutionSlider.Value;
        CabTexResolutionRow.ValueText = $"{resolution} px";
    }

    private void shaderResolutionSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (ShadowResolutionRow is null || shaderResolutionSlider is null)
            return;

        int resolution = 1 << (int)shaderResolutionSlider.Value;
        ShadowResolutionRow.ValueText = $"{resolution} px";
        ToolTip.SetTip(shaderResolutionSlider, ShadowMapWord(resolution));
    }

    private void FillLanguageCombo()
    {
        ChangeLanguageCb.Items.Clear();
        foreach (var lang in LocalizationService.AvailableLanguages())
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
    }

    private void LanguageComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if ((ChangeLanguageCb.SelectedItem as ComboBoxItem)?.Tag is not string code)
            return;
        if (string.Equals(code, App.Loc.CurrentLangCode, StringComparison.OrdinalIgnoreCase))
            return;

        App.ApplyLanguage(code);
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
        if (ReferenceEquals(row, FeedbackPortRow))
            return FeedbackCb.SelectedIndex == 3;
        if (ReferenceEquals(row, ComButtonRow))
            return FeedbackCb.SelectedIndex == 5;
        return true;
    }

    // ── Category navigation, search and unsaved-changes state ────────────────

    private bool _dirty;

    private void CategoryList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Picking a category is how you leave a search, so the query goes with it.
        if (SettingsSearch is { Text.Length: > 0 })
            SettingsSearch.Text = string.Empty;   // raises TextChanged -> ApplyFilter
        else
            ApplyFilter();
    }

    private void SettingsSearch_OnTextChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    /// <summary>
    /// Single owner of what is on screen. With an empty query it shows the selected
    /// category; with a query it stacks every category and keeps only the rows that
    /// match, collapsing sections and categories that end up empty.
    /// </summary>
    private void ApplyFilter()
    {
        if (SettingsStack is null || CategoryList is null || ControlsHost is null)
            return;

        string query = SettingsSearch?.Text?.Trim() ?? string.Empty;
        bool searching = query.Length > 0;
        string category = (CategoryList.SelectedItem as ListBoxItem)?.Tag as string ?? string.Empty;

        // Key bindings are their own world: they bring their own scrolling and a
        // star-sized row, so they replace the settings scroller rather than living
        // in it, and the search box filters them instead of the settings.
        bool bindings = category == "controls";
        ControlsHost.IsVisible = bindings;
        SettingsScroll.IsVisible = !bindings;

        if (bindings)
        {
            RebuildBindingList();
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
                // Choosing a profile changes nothing until Apply is pressed.
                case ComboBox combo when ReferenceEquals(combo, ProfileCb):
                    break;
                case CheckBox cb:
                    cb.IsCheckedChanged += (_, _) => MarkDirty();
                    break;
                case Slider slider:
                    slider.ValueChanged += (_, _) => MarkDirty();
                    break;
                case ComboBox combo:
                    combo.SelectionChanged += (_, _) => MarkDirty();
                    break;
                case NumericUpDown nud:
                    nud.ValueChanged += (_, _) => MarkDirty();
                    break;
            }
        }

        foreach (var section in this.GetLogicalDescendants().OfType<SettingsSection>())
            if (section.Toggle is CheckBox toggle)
                toggle.IsCheckedChanged += (_, _) => MarkDirty();
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
        if (SaveButton is null || SaveStatus is null)
            return;

        bool dirty = _dirty || KeyboardConfig.Instance.Dirty;
        SaveButton.Classes.Set("Accent", dirty);
        SaveStatus.Text = dirty ? App.Loc["UnsavedChanges"] : string.Empty;
    }

    private static bool IsChecked(CheckBox cb) => cb.IsChecked == true;

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

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

    private static int AnisotropyToSlider(int anisotropy)
    {
        var steps = SimulatorSettings.AnisotropySteps;
        for (int i = 0; i < steps.Length; i++)
            if (steps[i] == anisotropy)
                return i + 1;
        return 4;
    }

    private StackPanel? _bindingListPanel;
    private StackPanel? _keyboardPanel;

    /// <summary>
    /// True while a cab-control binding is waiting for a keypress. The window's own
    /// shortcuts stand down for the duration, otherwise Ctrl+N would never reach here.
    /// </summary>
    public bool IsCapturingKey => _capturing is not null;

    private KeyBinding? _capturing;
    private Button? _capturingButton;

    private readonly Dictionary<string, Grid> _keyCells = new(StringComparer.OrdinalIgnoreCase);

    private bool _flyoutLoading;

    private static readonly IBrush KeyUnassignedBrush = new SolidColorBrush(Color.Parse("#2A3036"));
    private static readonly IBrush KeyFillerBrush = new SolidColorBrush(Color.Parse("#21262B"));
    private static readonly IBrush KeyPlainBrush = new SolidColorBrush(Color.Parse("#2E9E1F"));
    private static readonly IBrush KeyShiftBrush = new SolidColorBrush(Color.Parse("#C9A227"));
    private static readonly IBrush KeyCtrlBrush = new SolidColorBrush(Color.Parse("#2D7FD3"));
    private static readonly IBrush KeyBorderBrush = new SolidColorBrush(Color.Parse("#3A424A"));
    private static readonly IBrush FgBrush = new SolidColorBrush(Color.Parse("#E6E8EA"));
    private static readonly IBrush FgDimBrush = new SolidColorBrush(Color.Parse("#9098A0"));
    private static readonly IBrush ConflictBrush = new SolidColorBrush(Color.Parse("#E06C5A"));

    private const double KeyUnit = 30;
    private const double KeyHeight = 34;
    private const double KeyGap = 4;

    private void BuildControlsTab()
    {
        if (ControlsHost is null)
            return;

        var topBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        topBar.Children.Add(new TextBlock
        {
            Text = App.Loc["BindingsHint"],
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = FgDimBrush
        });
        var restoreBtn = new Button { Content = App.Loc["RestoreDefaults"] };
        restoreBtn.Classes.Add("Flat");
        restoreBtn.Click += (_, _) =>
        {
            CancelCapture();
            KeyboardConfig.Instance.LoadDefaults();
            KeyboardConfig.Instance.Dirty = true;
            MarkDirty();
            RebuildBindingList();
            RebuildKeyboard();
        };
        topBar.Children.Add(restoreBtn);
        Grid.SetRow(topBar, 0);
        ControlsHost.Children.Add(topBar);

        _bindingListPanel = new StackPanel { Spacing = 3 };
        var listScroll = new ScrollViewer
        {
            Content = _bindingListPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(listScroll, 1);
        ControlsHost.Children.Add(listScroll);

        var bottom = new StackPanel { Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        bottom.Children.Add(BuildLegend());
        _keyboardPanel = new StackPanel();
        bottom.Children.Add(new ScrollViewer
        {
            Content = _keyboardPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        Grid.SetRow(bottom, 2);
        ControlsHost.Children.Add(bottom);

        RebuildBindingList();
        RebuildKeyboard();
    }

    private void RebuildBindingList()
    {
        if (_bindingListPanel is null)
            return;

        _bindingListPanel.Children.Clear();
        // The sidebar search box serves this list too - see ApplyFilter.
        string filter = SettingsSearch?.Text?.Trim() ?? string.Empty;
        var conflicts = ComputeConflicts();

        foreach (var b in KeyboardConfig.Instance.Bindings)
        {
            if (filter.Length > 0 && !MatchesFilter(b, filter))
                continue;
            _bindingListPanel.Children.Add(BuildBindingRow(b, conflicts));
        }
    }

    private static bool MatchesFilter(KeyBinding b, string filter) =>
        TextMatch.Contains(b.Command, filter) ||
        TextMatch.Contains(b.Description, filter);

    private Control BuildBindingRow(KeyBinding b, HashSet<string> conflicts)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };

        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new TextBlock
        {
            Text = CommandLabel(b),
            Foreground = FgBrush,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        label.Children.Add(new TextBlock
        {
            Text = b.Command,
            Foreground = FgDimBrush,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        bool conflict = b.IsAssigned && conflicts.Contains(ComboKey(b));
        var comboButton = new Button
        {
            Content = _capturing == b ? App.Loc["PressKey"] : ComboText(b),
            MinWidth = 140,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = conflict ? ConflictBrush : FgBrush
        };
        comboButton.Classes.Add("Flat");
        if (conflict)
            ToolTip.SetTip(comboButton, App.Loc["ConflictTooltip"]);
        comboButton.Click += (_, _) => StartCapture(b, comboButton);
        Grid.SetColumn(comboButton, 1);
        grid.Children.Add(comboButton);

        var clearButton = new Button
        {
            Content = "✕",
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        clearButton.Classes.Add("Basic");
        ToolTip.SetTip(clearButton, App.Loc["ClearBinding"]);
        clearButton.Click += (_, _) =>
        {
            CancelCapture();
            b.Shift = b.Ctrl = false;
            b.Key = "none";
            KeyboardConfig.Instance.Dirty = true;
            MarkDirty();
            RebuildBindingList();
            RebuildKeyboard();
        };
        Grid.SetColumn(clearButton, 2);
        grid.Children.Add(clearButton);

        return new Border
        {
            Background = KeyUnassignedBrush,
            BorderBrush = KeyBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(8, 4),
            Child = grid
        };
    }

    private void StartCapture(KeyBinding b, Button button)
    {

        if (_capturing is not null && _capturingButton is not null)
            _capturingButton.Content = ComboText(_capturing);

        _capturing = b;
        _capturingButton = button;
        button.Content = App.Loc["PressKey"];
        button.Focus();
    }

    private void CancelCapture()
    {
        if (_capturing is not null && _capturingButton is not null)
            _capturingButton.Content = ComboText(_capturing);
        _capturing = null;
        _capturingButton = null;
    }

    private void OnControlsKeyDown(object? sender, KeyEventArgs e)
    {
        if (_capturing is null)
            return;

        if (e.Key == Key.Escape)
        {
            CancelCapture();
            e.Handled = true;
            return;
        }

        if (KeyMap.IsModifierKey(e.Key))
            return;

        string? token = KeyMap.FromInput(e.Key, e.PhysicalKey);
        if (token is null)
        {
            e.Handled = true;
            return;
        }

        _capturing.Shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _capturing.Ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        _capturing.Key = token;
        KeyboardConfig.Instance.Dirty = true;
        MarkDirty();
        e.Handled = true;

        _capturing = null;
        _capturingButton = null;
        RebuildBindingList();
        RebuildKeyboard();
    }

    private void RebuildKeyboard()
    {
        if (_keyboardPanel is null)
            return;

        _keyCells.Clear();
        _keyboardPanel.Children.Clear();
        var states = ComputeKeyStates();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        row.Children.Add(BuildKeyBlock(KeyMap.MainBlock, states));
        row.Children.Add(BuildKeyBlock(KeyMap.NavBlock, states));
        row.Children.Add(BuildKeyBlock(KeyMap.NumpadBlock, states));
        _keyboardPanel.Children.Add(row);
    }

    private void UpdateKeyboardColors()
    {
        var states = ComputeKeyStates();
        foreach (var (token, content) in _keyCells)
        {
            if (content.Children.Count > 0)
                content.Children.RemoveAt(0);
            content.Children.Insert(0, BuildKeyBackground(token, states));
            states.TryGetValue(token, out var state);
            ToolTip.SetTip(content, BuildKeyTooltip(token, state));
        }
    }

    private Control BuildKeyBlock(KeyCap[][] rows, Dictionary<string, KeyState> states)
    {
        var block = new StackPanel { Spacing = KeyGap, VerticalAlignment = VerticalAlignment.Top };
        foreach (var rowCaps in rows)
        {
            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = KeyGap };
            foreach (var cap in rowCaps)
                rowPanel.Children.Add(BuildKeyCap(cap, states));
            block.Children.Add(rowPanel);
        }
        return block;
    }

    private Control BuildKeyCap(KeyCap cap, Dictionary<string, KeyState> states)
    {
        double width = cap.Width * KeyUnit + (cap.Width - 1) * KeyGap;
        var content = new Grid { Width = width, Height = KeyHeight };

        var keyBorder = new Border
        {
            BorderBrush = KeyBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            ClipToBounds = true,
            Child = content
        };

        if (cap.Token is null)
        {

            content.Children.Add(new Border { Background = KeyFillerBrush });
            content.Children.Add(new TextBlock
            {
                Text = cap.Label,
                Foreground = FgDimBrush,
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            keyBorder.Opacity = 0.55;
            if (!string.IsNullOrEmpty(cap.Label))
                ToolTip.SetTip(keyBorder, App.Loc["KbReserved"]);
            return keyBorder;
        }

        string token = cap.Token;
        states.TryGetValue(token.ToLowerInvariant(), out var st);
        content.Children.Add(BuildKeyBackground(token, states));
        content.Children.Add(new TextBlock
        {
            Text = cap.Label,
            Foreground = FgBrush,
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        ToolTip.SetTip(content, BuildKeyTooltip(token, st));

        keyBorder.Cursor = new Cursor(StandardCursorType.Hand);
        keyBorder.PointerPressed += (_, _) => ShowKeyFlyout(token, keyBorder);
        _keyCells[token] = content;
        return keyBorder;
    }

    private Control BuildKeyBackground(string token, Dictionary<string, KeyState> states)
    {
        states.TryGetValue(token.ToLowerInvariant(), out var state);
        var colours = new List<IBrush>();
        if (state is { Plain: true }) colours.Add(KeyPlainBrush);
        if (state is { Shift: true }) colours.Add(KeyShiftBrush);
        if (state is { Ctrl: true }) colours.Add(KeyCtrlBrush);

        if (colours.Count == 0)
            return new Border { Background = KeyUnassignedBrush };

        var stripes = new UniformGrid { Rows = 1, Columns = colours.Count };
        foreach (var c in colours)
            stripes.Children.Add(new Border { Background = c });
        return stripes;
    }

    private string BuildKeyTooltip(string token, KeyState? state)
    {
        string head = KeyMap.DisplayName(token);
        if (state is null || state.Tips.Count == 0)
            return $"{head}: {App.Loc["KbUnassigned"]}";
        return head + "\n" + string.Join("\n", state.Tips);
    }

    private void ShowKeyFlyout(string token, Control anchor)
    {
        CancelCapture();
        var commands = KeyboardConfig.Instance.Bindings;

        var items = new List<string> { App.Loc["BindNone"] };
        foreach (var c in commands)
            items.Add(CommandLabel(c));

        var slots = new (bool shift, bool ctrl, string labelKey)[]
        {
            (false, false, "BindNoMod"),
            (true,  false, "BindShift"),
            (false, true,  "BindCtrl"),
            (true,  true,  "BindShiftCtrl"),
        };

        var combos = new ComboBox[slots.Length];
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto")
        };

        for (int i = 0; i < slots.Length; i++)
        {
            var (shift, ctrl, labelKey) = slots[i];

            var lbl = new TextBlock
            {
                Text = App.Loc[labelKey],
                Foreground = FgBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 12, 4)
            };
            Grid.SetRow(lbl, i);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var cb = new ComboBox
            {
                ItemsSource = items,
                MinWidth = 280,
                MaxDropDownHeight = 320,
                Margin = new Thickness(0, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            cb.SelectionChanged += (_, _) =>
            {
                if (_flyoutLoading)
                    return;
                AssignSlot(token, shift, ctrl, cb.SelectedIndex, commands);
                SyncFlyoutCombos(token, combos);
                UpdateKeyboardColors();
                RebuildBindingList();
            };
            Grid.SetRow(cb, i);
            Grid.SetColumn(cb, 1);
            grid.Children.Add(cb);
            combos[i] = cb;
        }

        SyncFlyoutCombos(token, combos);

        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(12), MinWidth = 360 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{App.Loc["BindKeyTitle"]} {KeyMap.DisplayName(token)}",
            FontWeight = FontWeight.SemiBold,
            Foreground = FgBrush
        });
        panel.Children.Add(new TextBlock
        {
            Text = App.Loc["BindKeyHint"],
            Foreground = FgDimBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(grid);

        var flyout = new Flyout { Content = panel };
        flyout.ShowAt(anchor);
    }

    private void SyncFlyoutCombos(string token, ComboBox[] combos)
    {
        var commands = KeyboardConfig.Instance.Bindings;
        var slots = new (bool shift, bool ctrl)[] { (false, false), (true, false), (false, true), (true, true) };
        _flyoutLoading = true;
        for (int i = 0; i < combos.Length; i++)
        {
            var cmd = CommandInSlot(token, slots[i].shift, slots[i].ctrl);
            combos[i].SelectedIndex = cmd is null ? 0 : commands.IndexOf(cmd) + 1;
        }
        _flyoutLoading = false;
    }

    private void AssignSlot(string token, bool shift, bool ctrl, int selectedIndex, List<KeyBinding> commands)
    {
        KeyBinding? chosen = selectedIndex <= 0 ? null : commands[selectedIndex - 1];

        foreach (var b in KeyboardConfig.Instance.Bindings)
        {
            if (!ReferenceEquals(b, chosen) && b.IsAssigned &&
                string.Equals(b.Key, token, StringComparison.OrdinalIgnoreCase) &&
                b.Shift == shift && b.Ctrl == ctrl)
            {
                b.Shift = b.Ctrl = false;
                b.Key = "none";
            }
        }

        if (chosen is not null)
        {
            chosen.Key = token;
            chosen.Shift = shift;
            chosen.Ctrl = ctrl;
        }

        KeyboardConfig.Instance.Dirty = true;
        MarkDirty();
    }

    private static KeyBinding? CommandInSlot(string token, bool shift, bool ctrl) =>
        KeyboardConfig.Instance.Bindings.FirstOrDefault(b =>
            b.IsAssigned &&
            string.Equals(b.Key, token, StringComparison.OrdinalIgnoreCase) &&
            b.Shift == shift && b.Ctrl == ctrl);

    private Control BuildLegend()
    {
        var legend = new WrapPanel { Orientation = Orientation.Horizontal };
        legend.Children.Add(LegendItem(KeyUnassignedBrush, App.Loc["KbUnassigned"]));
        legend.Children.Add(LegendItem(KeyPlainBrush, App.Loc["KbAssigned"]));
        legend.Children.Add(LegendItem(KeyShiftBrush, App.Loc["KbShift"]));
        legend.Children.Add(LegendItem(KeyCtrlBrush, App.Loc["KbCtrl"]));
        return legend;
    }

    private Control LegendItem(IBrush brush, string text)
    {
        var sp = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        sp.Children.Add(new Border
        {
            Width = 16,
            Height = 16,
            Background = brush,
            BorderBrush = KeyBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center
        });
        sp.Children.Add(new TextBlock { Text = text, Foreground = FgDimBrush, VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }

    private sealed class KeyState
    {
        public bool Plain;
        public bool Shift;
        public bool Ctrl;
        public readonly List<string> Tips = new();
    }

    private static Dictionary<string, KeyState> ComputeKeyStates()
    {
        var map = new Dictionary<string, KeyState>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in KeyboardConfig.Instance.Bindings)
        {
            if (!b.IsAssigned)
                continue;
            string key = b.Key.ToLowerInvariant();
            if (!map.TryGetValue(key, out var state))
                map[key] = state = new KeyState();

            if (!b.Shift && !b.Ctrl) state.Plain = true;
            if (b.Shift) state.Shift = true;
            if (b.Ctrl) state.Ctrl = true;

            state.Tips.Add($"{ComboText(b)} — {CommandLabel(b)}");
        }
        return map;
    }

    private static HashSet<string> ComputeConflicts()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in KeyboardConfig.Instance.Bindings)
        {
            if (!b.IsAssigned)
                continue;
            string k = ComboKey(b);
            if (!seen.Add(k))
                dup.Add(k);
        }
        return dup;
    }

    private static string ComboKey(KeyBinding b) =>
        $"{(b.Ctrl ? 1 : 0)}|{(b.Shift ? 1 : 0)}|{b.Key.ToLowerInvariant()}";

    private static string ComboText(KeyBinding b)
    {
        if (!b.IsAssigned)
            return "—";
        var parts = new List<string>();
        if (b.Ctrl) parts.Add("Ctrl");
        if (b.Shift) parts.Add("Shift");
        parts.Add(KeyMap.DisplayName(b.Key));
        return string.Join(" + ", parts);
    }

    private static string CommandLabel(KeyBinding b) =>
        string.IsNullOrEmpty(b.Description) ? b.Command : Capitalize(b.Description);

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    private static string[] QualityWords => new[]
    {
        App.Loc["QualityVeryLow"], App.Loc["QualityLow"], App.Loc["QualityNormal"],
        App.Loc["QualityHigh"], App.Loc["QualityVeryHigh"]
    };

    private void QualitySlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        => UpdateQualityCaptions();

    private void UpdateQualityCaptions()
    {
        var q = QualityWords;

        if (TexFilteringRow != null)
            TexFilteringRow.ValueText = q[Clamp((int)TexFilteringSlider.Value, 1, 5) - 1];

        string[] four = { q[0], q[1], q[3], q[4] };
        if (TrackCurvesRow != null)
            TrackCurvesRow.ValueText = four[Clamp((int)TrackCurvesSlider.Value, 1, 4) - 1];
        if (SmokeParticlesRow != null)
            SmokeParticlesRow.ValueText = four[Clamp((int)SmokeParticlesSlider.Value, 1, 4) - 1];

        string[] msaa = { App.Loc["MsaaNone"], "x2", "x4", "x8" };
        if (MultisamplingRow != null)
            MultisamplingRow.ValueText = msaa[Clamp((int)MultisamplingSlider.Value, 1, 4) - 1];

        string[] range = { App.Loc["RangeNormal"], App.Loc["RangeHigh"], App.Loc["RangeVeryHigh"] };
        if (RenderRangeRow != null)
            RenderRangeRow.ValueText = range[Clamp((int)RenderRangeSlider.Value, 1, 3) - 1];

        if (CursorSensitivityRow != null)
            CursorSensitivityRow.ValueText = (int)CursorSensitivitySlider.Value switch
            {
                <= 1 => q[1],
                2 => App.Loc["QualityStandard"],
                3 => q[3],
                _ => q[4]
            };

        if (ShadowRangeRow != null)
        {
            int metres = (int)shaderRange.Value;
            ShadowRangeRow.ValueText = $"{metres} m";
            ToolTip.SetTip(shaderRange, ShadowRangeWord(metres));
        }

        if (CabShadowRangeRow != null)
        {
            int metres = (int)cabShaderSourceRange.Value;
            CabShadowRangeRow.ValueText = metres <= 0 ? App.Loc["RangeDisabled"] : $"{metres} m";
            ToolTip.SetTip(cabShaderSourceRange, CabShadowRangeWord(metres));
        }

        if (ReflectionsFramerateRow != null)
        {
            int fps = (int)reflectionsFramerate.Value;
            string word = ReflectionsRefreshWord(fps);
            ReflectionsFramerateRow.ValueText = $"{fps} FPS";
            ToolTip.SetTip(reflectionsFramerate, word.Length == 0 ? null : word);
        }
    }

    private static string ShadowMapWord(int pixels) => pixels switch
    {
        <= 512 => App.Loc["QualityVeryLow"],
        <= 1024 => App.Loc["QualityLow"],
        <= 2048 => App.Loc["QualityModerate"],
        <= 4096 => App.Loc["QualityHigh"],
        _ => App.Loc["QualityVeryHigh"]
    };

    private static string ShadowRangeWord(int metres) => metres switch
    {
        <= 25 => App.Loc["RangeVeryLow"],
        <= 50 => App.Loc["RangeLow"],
        <= 150 => App.Loc["RangeModerate"],
        <= 250 => App.Loc["RangeHigh"],
        _ => App.Loc["RangeVeryHigh"]
    };

    private static string CabShadowRangeWord(int metres) => metres switch
    {
        <= 10 => App.Loc["RangeVeryLow"],
        <= 20 => App.Loc["RangeLow"],
        <= 30 => App.Loc["RangeStandard"],
        <= 50 => App.Loc["RangeHigh"],
        _ => App.Loc["RangeVeryHigh"]
    };

    private static string ReflectionsRefreshWord(int fps) => fps switch
    {
        <= 5 => App.Loc["QualityVeryLow"],
        <= 10 => App.Loc["QualityLow"],
        <= 25 => App.Loc["QualityHigh"],
        <= 60 => App.Loc["QualityVeryHigh"],
        _ => string.Empty
    };

}
