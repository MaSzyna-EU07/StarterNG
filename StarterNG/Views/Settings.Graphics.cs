using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using StarterNG.Application;
using StarterNG.Domain.Settings;

namespace StarterNG.Views;

// The graphics and effects categories: their controls, the dependencies between
// them (what the simulator only does on Vulkan, with DLSS, ...), the quality presets
// and the import and export of eu07.ini.
public partial class Settings
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly FilePickerFileType[] IniTypes =
    {
        new("eu07.ini") { Patterns = new[] { "*.ini" } },
        new("*") { Patterns = new[] { "*" } }
    };

    /// <summary>Set while the dependencies are brought in line, so the changes they make do not recurse.</summary>
    private bool _updatingGraphics;

    /// <summary>
    /// What only Vulkan has, as it was when OpenGL got chosen, so that going back to
    /// Vulkan brings it back instead of leaving it switched off.
    /// </summary>
    private (AntiAliasingMode AntiAliasing, int FrameGeneration, int Reflex, bool RayTracing)? _vulkanChoices;

    private string? _dlssProbedFor;
    private (bool Dlss, bool FrameGeneration) _dlssProbe;

    private static bool DlssPlatform => AppServices.Current.SettingsStore.DlssPlatform;

    private void InitGraphics()
    {
        RenderEngineCb.SelectionChanged += (_, _) => OnBackendChanged();
        // The notes are worked out here, not bound, so they follow a change of language by hand.
        App.Loc.LanguageChanged += UpdateGraphicsState;
    }

    private void ApplyGraphicsToUi(SimulatorSettings s)
    {
        RenderEngineCb.SelectedIndex = (int)s.Backend;
        VulkanDeviceTb.Text = s.VulkanDevice;
        SelectResolution(s.Width, s.Height);
        FullscreenWindowedCb.IsChecked = s.FullscreenWindowed;
        bufferScale.Value = s.BufferScalePercent;
        SelectTag(AntiAliasingCb, AntiAliasingTag(s.AntiAliasing));
        SelectTag(DlssPresetCb, s.DlssPreset);
        SelectTag(FrameGenerationCb, s.FrameGeneration.ToString(Invariant));
        SelectTag(ReflexCb, s.Reflex.ToString(Invariant));

        textureResolutionSlider.Value = Log2(s.MaxTextureSize, 12);
        cabTextureResolutionSlider.Value = Log2(s.MaxCabTextureSize, 12);
        TexFilteringSlider.Value = AnisotropyToSlider(s.TextureFiltering);
        TrackCurvesSlider.Value = s.SplineFidelity;
        RenderRangeSlider.Value = (int)Math.Round(s.DrawRangeFactor);

        VSyncCb.IsChecked = s.VSync;
        FpsLimitEnableCb.IsChecked = s.FpsLimitEnabled;
        FpsLimitSlider.Value = s.FpsLimit;
        FpsLimitSlider.IsEnabled = s.FpsLimitEnabled;
        fovSlider.Value = s.FieldOfView;
        AdditionalShadersCb.IsChecked = s.ExtraEffects;
        SkipPipelineCb.IsChecked = s.SkipPipeline;

        RenderShadowsCb.IsChecked = s.RenderShadows;
        ShadowDisplayCb.SelectedIndex = Clamp(s.ShadowRankCutoff - 1, 0, 2);
        shaderResolutionSlider.Value = Log2(s.ShadowMapResolution, 11);
        shaderRange.Value = s.ShadowProjectionRange;
        ShadowSdsmCb.IsChecked = s.ShadowSdsm;
        ShadowAngleLimitSlider.Value = s.ShadowAngleLimit;

        LightsClusteredCb.IsChecked = s.LightsClustered;
        DynamicLightsSlider.Value = s.DynamicLights;
        SelectTag(LightShadowsCb, s.LightShadows.ToString(Invariant));
        SelectTag(ContactShadowsCb, s.ContactShadows.ToString(Invariant));
        SelectTag(AmbientOcclusionCb, s.AmbientOcclusion.ToString(Invariant));
        SelectOrAddTag(AoQualityCb, $"{s.AoSlices} {s.AoSteps}", $"{s.AoSlices} / {s.AoSteps}");

        ReflectionsCubeMapCb.IsChecked = s.EnvMap;
        SelectTag(SsrCb, s.Ssr.ToString(Invariant));
        RayTracingCb.IsChecked = s.RayTracing;
        RayTracingRangeSlider.Value = s.RayTracingRange;
        ReflectionsDetailsCb.SelectedIndex = s.ReflectionsFidelity;
        reflectionsFramerate.Value = s.ReflectionsFramerate;

        VolumetricFogCb.IsChecked = s.VolumetricFog;
        SelectTag(FogQualityCb, s.FogQuality.ToString(Invariant));
        SelectTag(CloudsCb, s.Clouds.ToString(Invariant));
        CloudShadowsCb.IsChecked = s.CloudShadows;

        BloomCb.IsChecked = s.Bloom;
        AutoExposureCb.IsChecked = s.AutoExposure;
        MotionBlurCb.IsChecked = s.MotionBlur;
        ChromaticAberrationCb.IsChecked = s.ChromaticAberration;

        GrassCb.IsChecked = s.Grass;
        GrassDistanceSlider.Value = s.GrassDistance;
        GrassDensitySlider.Value = s.GrassDensity;
        SmokeDisplayCb.IsChecked = s.Smoke;
        SmokeParticlesSlider.Value = s.SmokeFidelity;
        string particles = s.PrecipitationParticles.ToString(Invariant);
        SelectOrAddTag(PrecipitationCb, particles, particles);
    }

    private void CaptureGraphicsInto(SimulatorSettings s)
    {
        s.Backend = RenderEngineCb.SelectedIndex == 1 ? GraphicsBackend.Vulkan : GraphicsBackend.OpenGL;
        s.VulkanDevice = VulkanDeviceTb.Text?.Trim() ?? "";
        ReadResolution(s);
        s.FullscreenWindowed = IsChecked(FullscreenWindowedCb);
        s.BufferScalePercent = (int)bufferScale.Value;
        s.AntiAliasing = AntiAliasingFromTag(SelectedTag(AntiAliasingCb));
        s.DlssPreset = SelectedTag(DlssPresetCb) ?? "default";
        s.FrameGeneration = TagInt(FrameGenerationCb, 1);
        s.Reflex = TagInt(ReflexCb, 0);

        s.MaxTextureSize = 1 << (int)textureResolutionSlider.Value;
        s.MaxCabTextureSize = 1 << (int)cabTextureResolutionSlider.Value;
        s.TextureFiltering = SimulatorSettings.AnisotropySteps[
            Clamp((int)TexFilteringSlider.Value - 1, 0, SimulatorSettings.AnisotropySteps.Length - 1)];
        s.SplineFidelity = (int)TrackCurvesSlider.Value;
        s.DrawRangeFactor = RenderRangeSlider.Value;

        s.VSync = IsChecked(VSyncCb);
        s.FpsLimitEnabled = IsChecked(FpsLimitEnableCb);
        s.FpsLimit = (int)FpsLimitSlider.Value;
        s.FieldOfView = Clamp((int)fovSlider.Value, 15, 75);
        s.ExtraEffects = IsChecked(AdditionalShadersCb);
        s.SkipPipeline = IsChecked(SkipPipelineCb);

        s.RenderShadows = IsChecked(RenderShadowsCb);
        s.ShadowRankCutoff = Math.Max(0, ShadowDisplayCb.SelectedIndex) + 1;
        s.ShadowMapResolution = 1 << (int)shaderResolutionSlider.Value;
        s.ShadowProjectionRange = (int)shaderRange.Value;
        s.ShadowSdsm = IsChecked(ShadowSdsmCb);
        s.ShadowAngleLimit = ShadowAngleLimitSlider.Value;

        s.LightsClustered = IsChecked(LightsClusteredCb);
        s.DynamicLights = (int)DynamicLightsSlider.Value;
        s.LightShadows = TagInt(LightShadowsCb, 2);
        s.ContactShadows = TagInt(ContactShadowsCb, 0);
        s.AmbientOcclusion = TagInt(AmbientOcclusionCb, 2);
        var ao = (SelectedTag(AoQualityCb) ?? "3 4").Split(' ');
        s.AoSlices = ao.Length > 0 && int.TryParse(ao[0], NumberStyles.Integer, Invariant, out int slices) ? slices : 3;
        s.AoSteps = ao.Length > 1 && int.TryParse(ao[1], NumberStyles.Integer, Invariant, out int steps) ? steps : 4;

        s.EnvMap = IsChecked(ReflectionsCubeMapCb);
        s.Ssr = TagInt(SsrCb, 2);
        s.RayTracing = IsChecked(RayTracingCb);
        s.RayTracingRange = (int)RayTracingRangeSlider.Value;
        s.ReflectionsFidelity = Math.Max(0, ReflectionsDetailsCb.SelectedIndex);
        s.ReflectionsFramerate = (int)reflectionsFramerate.Value;

        s.VolumetricFog = IsChecked(VolumetricFogCb);
        s.FogQuality = TagInt(FogQualityCb, 2);
        s.Clouds = TagInt(CloudsCb, 2);
        s.CloudShadows = IsChecked(CloudShadowsCb);

        s.Bloom = IsChecked(BloomCb);
        s.AutoExposure = IsChecked(AutoExposureCb);
        s.MotionBlur = IsChecked(MotionBlurCb);
        s.ChromaticAberration = IsChecked(ChromaticAberrationCb);

        s.Grass = IsChecked(GrassCb);
        s.GrassDistance = (int)GrassDistanceSlider.Value;
        s.GrassDensity = GrassDensitySlider.Value;
        s.Smoke = IsChecked(SmokeDisplayCb);
        s.SmokeFidelity = (int)SmokeParticlesSlider.Value;
        s.PrecipitationParticles = TagInt(PrecipitationCb, 65536);
    }

    /// <summary>The settings as this screen shows them, edits included, without touching the real ones.</summary>
    private SimulatorSettings SnapshotFromUi()
    {
        var s = new SimulatorSettings();
        CaptureInto(s);
        return s;
    }

    private void OnBackendChanged()
    {
        if (_loading || _updatingGraphics)
            return;

        _updatingGraphics = true;
        try
        {
            if (RenderEngineCb.SelectedIndex != 1)
            {
                var s = SnapshotFromUi();
                _vulkanChoices ??= (s.AntiAliasing, s.FrameGeneration, s.Reflex, s.RayTracing);
                if (GraphicsRules.IsDlss(s.AntiAliasing))
                    SelectTag(AntiAliasingCb, "taa");
                FrameGenerationCb.SelectedIndex = 0;
                ReflexCb.SelectedIndex = 0;
                RayTracingCb.IsChecked = false;
            }
            else if (_vulkanChoices is { } kept)
            {
                if (GraphicsRules.IsDlss(kept.AntiAliasing) && DlssPlatform && SelectedTag(AntiAliasingCb) == "taa")
                    SelectTag(AntiAliasingCb, AntiAliasingTag(kept.AntiAliasing));
                SelectTag(FrameGenerationCb, kept.FrameGeneration.ToString(Invariant));
                SelectTag(ReflexCb, kept.Reflex.ToString(Invariant));
                RayTracingCb.IsChecked = kept.RayTracing;
                _vulkanChoices = null;
            }
        }
        finally
        {
            _updatingGraphics = false;
        }

        UpdateGraphicsState();
    }

    /// <summary>
    /// Brings the graphics controls in line with one another the way the simulator
    /// treats them, says why something is off, and shows which preset the settings are.
    /// </summary>
    private void UpdateGraphicsState()
    {
        if (_loading || _updatingGraphics || AntiAliasingCb is null)
            return;

        _updatingGraphics = true;
        try
        {
            var s = SnapshotFromUi();
            bool vulkan = GraphicsRules.IsVulkan(s);
            bool dlssAllowed = GraphicsRules.DlssAllowed(s, DlssPlatform);

            foreach (var item in AntiAliasingCb.Items.OfType<ComboBoxItem>())
                if (item.Tag is "dlss" or "dlaa")
                    item.IsEnabled = dlssAllowed;
            if (GraphicsRules.IsDlss(s.AntiAliasing) && !dlssAllowed)
                SelectTag(AntiAliasingCb, "taa");

            s = SnapshotFromUi();
            bool dlss = GraphicsRules.DlssActive(s, DlssPlatform);
            DlssPresetRow.IsEnabled = dlss;
            FrameGenerationRow.IsEnabled = dlss;
            if (!dlss && FrameGenerationCb.SelectedIndex > 0)
                FrameGenerationCb.SelectedIndex = 0;
            ReflexRow.IsEnabled = vulkan;
            if (!vulkan && ReflexCb.SelectedIndex > 0)
                ReflexCb.SelectedIndex = 0;
            VulkanDeviceRow.IsEnabled = vulkan;
            RayTracingRow.IsEnabled = vulkan;
            if (!vulkan)
                RayTracingCb.IsChecked = false;

            s = SnapshotFromUi();
            BufferScaleRow.IsEnabled = GraphicsRules.BufferScaleUsed(s, DlssPlatform);
            RayTracingRangeRow.IsEnabled = vulkan && s.RayTracing;
            DynamicLightsRow.IsEnabled = GraphicsRules.DynamicLightsUsed(s);
            AoQualityRow.IsEnabled = s.AmbientOcclusion > 0;
            FogQualityRow.IsEnabled = s.VolumetricFog;
            CloudShadowsRow.IsEnabled = s.Clouds > 0;
            GrassDistanceRow.IsEnabled = s.Grass;
            GrassDensityRow.IsEnabled = s.Grass;

            DlssPresetInfo.Text = App.Loc[(SelectedTag(DlssPresetCb) ?? "default") switch
            {
                "k" => "DlssPresetInfoK",
                "j" => "DlssPresetInfoJ",
                "m" => "DlssPresetInfoM",
                "l" => "DlssPresetInfoL",
                _ => "DlssPresetInfoDefault"
            }];
            SetNote(RenderEngineNote, vulkan ? App.Loc["REVulkanNote"] : App.Loc["REOpenGLNote"]);
            SetNote(AntiAliasingNote, AntiAliasingRemark(s, dlss));
            SetNote(FrameGenerationNote, FrameGenerationRemark(s, dlss));
            SetNote(ReflexNote, GraphicsRules.ReflexForced(s, DlssPlatform) ? App.Loc["ReflexForcedNote"] : "");
            SetNote(RayTracingNote, vulkan ? "" : App.Loc["VulkanOnlyNote"]);
            SetNote(SkipPipelineNote, GraphicsRules.TemporalWithoutPipeline(s) ? App.Loc["TemporalNeedsPipelineNote"] : "");

            QualityPresetCb.SelectedIndex = (int)GraphicsPresets.Detect(s, DlssPlatform);
        }
        finally
        {
            _updatingGraphics = false;
        }
    }

    private string AntiAliasingRemark(SimulatorSettings s, bool dlss)
    {
        if (!DlssPlatform && GraphicsRules.IsVulkan(s))
            return App.Loc["DlssNoPlatformNote"];
        if (GraphicsRules.TemporalWithoutPipeline(s))
            return App.Loc["TemporalNeedsPipelineNote"];
        if (dlss && !DlssLibraries(s).Dlss)
            return string.Format(App.Loc["DlssMissingLibraryNote"], DlssLibraryName(frameGeneration: false));
        return "";
    }

    private string FrameGenerationRemark(SimulatorSettings s, bool dlss)
    {
        if (!dlss)
            return GraphicsRules.IsVulkan(s) ? App.Loc["FrameGenerationNeedsDlssNote"] : "";
        if (s.FrameGeneration > 1 && !DlssLibraries(s).FrameGeneration)
            return string.Format(App.Loc["DlssMissingLibraryNote"], DlssLibraryName(frameGeneration: true));
        return "";
    }

    /// <summary>Looked up once per simulator picked: the search for it reads the installation's directory.</summary>
    private (bool Dlss, bool FrameGeneration) DlssLibraries(SimulatorSettings s)
    {
        string key = s.SelectExeAutomatically ? "\u0001auto" : s.ExecutablePath;
        if (_dlssProbedFor != key)
        {
            var executables = AppServices.Current.Executables;
            _dlssProbe = executables.DlssLibraries(executables.Resolve(s, out _));
            _dlssProbedFor = key;
        }
        return _dlssProbe;
    }

    private static string DlssLibraryName(bool frameGeneration) =>
        AppServices.Current.Environment.IsWindows
            ? (frameGeneration ? "nvngx_dlssg.dll" : "nvngx_dlss.dll")
            : (frameGeneration ? "libnvidia-ngx-dlssg.so" : "libnvidia-ngx-dlss.so");

    private void QualityPresetCb_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || _updatingGraphics || QualityPresetCb is null)
            return;

        int index = QualityPresetCb.SelectedIndex;
        if (index < 0 || index >= GraphicsPresets.All.Length)
            return;

        var s = SnapshotFromUi();
        GraphicsPresets.Apply(s, GraphicsPresets.All[index], DlssPlatform);

        _loading = true;
        try
        {
            ApplyGraphicsToUi(s);
        }
        finally
        {
            _loading = false;
        }

        UpdateQualityCaptions();
        MarkDirty();
        UpdateGraphicsState();
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

        _vulkanChoices = null;
        ApplyToUi();
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
        // user's own eu07.ini is not written.
        CaptureInto(AppServices.Current.Settings);
        bool ok = AppServices.Current.SettingsStore.ExportTo(path);
        if (!ok)
        {
            await MessageBox.Show(owner, string.Format(App.Loc["ExportIniFailed"], path), App.Loc["ExportIni"],
                                  MessageBoxButtons.Ok);
            return;
        }

        SaveStatus.Text = string.Format(App.Loc["ExportIniDone"], Path.GetFileName(path));
        ToolTip.SetTip(SaveStatus, path);
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
        AntiAliasingMode.Taa => "taa",
        AntiAliasingMode.Dlss => "dlss",
        AntiAliasingMode.Dlaa => "dlaa",
        _ => "off"
    };

    private static AntiAliasingMode AntiAliasingFromTag(string? tag) => tag switch
    {
        "msaax2" => AntiAliasingMode.Msaa2,
        "msaax4" => AntiAliasingMode.Msaa4,
        "msaax8" => AntiAliasingMode.Msaa8,
        "taa" => AntiAliasingMode.Taa,
        "dlss" => AntiAliasingMode.Dlss,
        "dlaa" => AntiAliasingMode.Dlaa,
        _ => AntiAliasingMode.Off
    };
}
