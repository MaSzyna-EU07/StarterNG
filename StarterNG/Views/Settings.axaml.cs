using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using StarterNG.Classes;

namespace StarterNG.Views;

public partial class Settings : UserControl
{
    private bool _loading;

    public Settings()
    {
        InitializeComponent();

        this.AttachedToVisualTree += (_, _) =>
        {
            TextureResolutionSlider_OnValueChanged(null, null);
            CabTextureResolutionSlider_OnValueChanged(null, null);
            shaderResolutionSlider_OnValueChanged(null, null);
        };

        ChangeLanguageCb.SelectedIndex = App.Loc.CurrentLanguage switch
        {
            "Polski" => 0,
            _ => 1
        };

        // The settings instance pulls live values from here whenever the app
        // closes or the game is launched.
        StarterNG.Classes.Settings.Instance.CaptureFromUi = ReadFromUi;

        ApplyToUi();
    }

    // ── Settings instance → controls ──────────────────────────────────────
    private void ApplyToUi()
    {
        var s = StarterNG.Classes.Settings.Instance;
        _loading = true;
        try
        {
            // General
            ChangeLanguageCb.SelectedIndex = s.Language == "Polski" ? 0 : 1;
            FullscreenCb.IsChecked = s.Fullscreen;
            PauseInactiveCb.IsChecked = s.PauseWhenInactive;
            PauseStartCb.IsChecked = s.PauseOnStart;
            CursorSensitivitySlider.Value = s.CursorSensitivity;
            MouseHorInvertCb.IsChecked = s.InvertMouseHorizontal;
            MouseVertInvertCb.IsChecked = s.InvertMouseVertical;

            // Communication
            GamepadIgnoreCb.IsChecked = s.IgnoreGamepad;
            FeedbackCb.SelectedIndex = s.FeedbackMode;

            // Other
            SelectExeCb.SelectedIndex = s.SelectExeAutomatically ? 0 : 1;
            DebugModeCb.IsChecked = s.DebugMode;
            VirtualShuntingCb.IsChecked = s.VirtualShunting;

            // Graphics
            RenderEngineCb.SelectedIndex = s.RenderEngine;
            SelectResolution(s.Width, s.Height);
            bufferScale.Value = s.BufferScalePercent;
            textureResolutionSlider.Value = Log2(s.MaxTextureSize, 12);
            cabTextureResolutionSlider.Value = Log2(s.MaxCabTextureSize, 12);
            TexFilteringSlider.Value = AnisotropyToSlider(s.TextureFiltering);
            MultisamplingSlider.Value = s.Multisampling + 1;
            RenderRangeSlider.Value = (int)Math.Round(s.DrawRangeFactor);
            VSyncCb.IsChecked = s.VSync;
            SmokeDisplayCb.IsChecked = s.Smoke;
            SmokeParticlesSlider.Value = s.SmokeFidelity;
            PostprocessingCb.SelectedIndex = s.Tonemapping;
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
            RenderScreensFramerateSlider.Value = s.ScreenRendererPriority;

            // Physics
            TrackCurvesSlider.Value = s.SplineFidelity;
            PhysicsAccuracyCb.IsChecked = s.FullPhysics;
            PantographBreakCb.IsChecked = s.EnableTraction;
            OverheadOnlyCb.IsChecked = s.LiveTraction;
            SpeedometerTapesCb.IsChecked = s.PhysicsLog;
            SimLogsCb.IsChecked = s.DebugLog;
            KeepLogsCb.IsChecked = s.MultipleLogs;
            DisplaySimulationCb.IsChecked = s.DisplaySimulation;
            CrashDamageCb.IsChecked = s.CrashDamage;

            // Sound
            EnableSoundsCb.IsChecked = s.SoundEnabled;
            VolumeSlider.Value = s.Volume;
            RadioVolumeSlider.Value = s.RadioVolume;
            VehiclesVolumeSlider.Value = s.VehiclesVolume;
            PositionalVolumeSlider.Value = s.PositionalVolume;
            AmbientVolumeSlider.Value = s.AmbientVolume;
            PauseVolumeSlider.Value = s.PausedVolume;

            // Starter
            AutoCloseStarterCb.IsChecked = s.AutoCloseStarter;
            LargeThumbnailsCb.IsChecked = s.LargeThumbnails;
            AutoExpandTreeCb.IsChecked = s.AutoExpandSceneryTree;
        }
        finally
        {
            _loading = false;
        }
    }

    // ── controls → Settings instance ──────────────────────────────────────
    private void ReadFromUi()
    {
        var s = StarterNG.Classes.Settings.Instance;

        // General
        s.Language = ChangeLanguageCb.SelectedIndex == 0 ? "Polski" : "English";
        s.Fullscreen = IsChecked(FullscreenCb);
        s.PauseWhenInactive = IsChecked(PauseInactiveCb);
        s.PauseOnStart = IsChecked(PauseStartCb);
        s.CursorSensitivity = (int)CursorSensitivitySlider.Value;
        s.InvertMouseHorizontal = IsChecked(MouseHorInvertCb);
        s.InvertMouseVertical = IsChecked(MouseVertInvertCb);

        // Communication
        s.IgnoreGamepad = IsChecked(GamepadIgnoreCb);
        s.FeedbackMode = Math.Max(0, FeedbackCb.SelectedIndex);

        // Other
        s.SelectExeAutomatically = SelectExeCb.SelectedIndex == 0;
        s.ExecutablePath = s.SelectExeAutomatically ? "eu07.exe"
            : (SelectExeCb.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "eu07.exe";
        s.DebugMode = IsChecked(DebugModeCb);
        s.VirtualShunting = IsChecked(VirtualShuntingCb);

        // Graphics
        s.RenderEngine = Math.Max(0, RenderEngineCb.SelectedIndex);
        ReadResolution(s);
        s.BufferScalePercent = (int)bufferScale.Value;
        s.MaxTextureSize = 1 << (int)textureResolutionSlider.Value;
        s.MaxCabTextureSize = 1 << (int)cabTextureResolutionSlider.Value;
        s.TextureFiltering = StarterNG.Classes.Settings.AnisotropySteps[
            Clamp((int)TexFilteringSlider.Value - 1, 0, StarterNG.Classes.Settings.AnisotropySteps.Length - 1)];
        s.Multisampling = Clamp((int)MultisamplingSlider.Value - 1, 0, 3);
        s.DrawRangeFactor = RenderRangeSlider.Value;
        s.VSync = IsChecked(VSyncCb);
        s.Smoke = IsChecked(SmokeDisplayCb);
        s.SmokeFidelity = (int)SmokeParticlesSlider.Value;
        s.Tonemapping = Math.Max(0, PostprocessingCb.SelectedIndex);
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
        s.ScreenRendererPriority = (int)RenderScreensFramerateSlider.Value;

        // Physics
        s.SplineFidelity = (int)TrackCurvesSlider.Value;
        s.FullPhysics = IsChecked(PhysicsAccuracyCb);
        s.EnableTraction = IsChecked(PantographBreakCb);
        s.LiveTraction = IsChecked(OverheadOnlyCb);
        s.PhysicsLog = IsChecked(SpeedometerTapesCb);
        s.DebugLog = IsChecked(SimLogsCb);
        s.MultipleLogs = IsChecked(KeepLogsCb);
        s.DisplaySimulation = IsChecked(DisplaySimulationCb);
        s.CrashDamage = IsChecked(CrashDamageCb);

        // Sound
        s.SoundEnabled = IsChecked(EnableSoundsCb);
        s.Volume = (int)VolumeSlider.Value;
        s.RadioVolume = (int)RadioVolumeSlider.Value;
        s.VehiclesVolume = (int)VehiclesVolumeSlider.Value;
        s.PositionalVolume = (int)PositionalVolumeSlider.Value;
        s.AmbientVolume = (int)AmbientVolumeSlider.Value;
        s.PausedVolume = (int)PauseVolumeSlider.Value;

        // Starter
        s.AutoCloseStarter = IsChecked(AutoCloseStarterCb);
        s.LargeThumbnails = IsChecked(LargeThumbnailsCb);
        s.AutoExpandSceneryTree = IsChecked(AutoExpandTreeCb);
    }

    private void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ReadFromUi();
        StarterNG.Classes.Settings.Instance.Save();
        if (SaveStatus is not null)
            SaveStatus.Text = App.Loc["SettingsSaved"];
    }

    private void ResetButton_OnClick(object? sender, RoutedEventArgs e)
    {
        StarterNG.Classes.Settings.Instance.Load();
        ApplyToUi();
        if (SaveStatus is not null)
            SaveStatus.Text = string.Empty;
    }

    // ── resolution combo helpers ──────────────────────────────────────────
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
        // Not in the predefined list: add and select it.
        var added = new ComboBoxItem { Content = target };
        ResolutionCb.Items.Add(added);
        ResolutionCb.SelectedItem = added;
    }

    private void ReadResolution(Classes.Settings s)
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

    // ── existing slider readouts ──────────────────────────────────────────
    private void TextureResolutionSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (texResolution is null || textureResolutionSlider is null)
            return;

        int resolution = 1 << (int)textureResolutionSlider.Value;
        texResolution.Text = $"{resolution} px";
    }

    private void CabTextureResolutionSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (cabTexResolution is null || cabTextureResolutionSlider is null)
            return;

        int resolution = 1 << (int)cabTextureResolutionSlider.Value;
        cabTexResolution.Text = $"{resolution} px";
    }

    private void shaderResolutionSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (shaderResolution is null || shaderResolutionSlider is null)
            return;

        int resolution = 1 << (int)shaderResolutionSlider.Value;
        shaderResolution.Text = $"{resolution} px";
    }

    private void LanguageComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: ComboBoxItem item }) return;
        if (!item.IsKeyboardFocusWithin) return; // ignore programmatic (ApplyToUi) changes
        var lang = item.Content?.ToString();
        if (string.IsNullOrEmpty(lang)) return;

        App.ApplyLanguage(lang);
    }

    // ── tiny helpers ──────────────────────────────────────────────────────
    private static bool IsChecked(CheckBox cb) => cb.IsChecked == true;

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

    private static int Log2(int value, int fallback)
    {
        if (value <= 0) return fallback;
        int log = (int)Math.Round(Math.Log2(value));
        return log;
    }

    private static int AnisotropyToSlider(int anisotropy)
    {
        var steps = StarterNG.Classes.Settings.AnisotropySteps;
        for (int i = 0; i < steps.Length; i++)
            if (steps[i] == anisotropy)
                return i + 1;
        return 4; // default → 8x
    }
}
