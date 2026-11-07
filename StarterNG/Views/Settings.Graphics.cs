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

// What the graphics and effects categories add to the screen: the controls of the settings the
// new renderers read, the dependencies between them (what the simulator only does on Vulkan,
// with DLSS, ...), and the import and export of eu07.ini.
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
        // The notes are worked out here, not bound, so they follow a change of language by hand.
        App.Loc.LanguageChanged += UpdateGraphicsState;
        SelectExeCb.SelectionChanged += (_, _) => UpdateGraphicsState();
    }

    private void ShowGraphics(SimulatorSettings s)
    {
        RenderEngineCb.SelectedIndex = s.RenderEngine;
        VulkanDeviceTb.Text = s.VulkanDevice;
        SelectTag(AntiAliasingCb, AntiAliasingTag(s.AntiAliasing));
        SelectTag(DlssPresetCb, s.DlssPreset);
        SelectTag(FrameGenerationCb, s.FrameGeneration.ToString(Invariant));
        SelectTag(ReflexCb, s.Reflex.ToString(Invariant));

        ShadowSdsmCb.IsChecked = s.ShadowSdsm;
        LightsClusteredCb.IsChecked = s.LightsClustered;
        SelectTag(LightShadowsCb, s.LightShadows.ToString(Invariant));
        SelectTag(ContactShadowsCb, s.ContactShadows.ToString(Invariant));
        SelectTag(AmbientOcclusionCb, s.AmbientOcclusion.ToString(Invariant));
        SelectOrAddTag(AoQualityCb, $"{s.AoSlices} {s.AoSteps}", $"{s.AoSlices} / {s.AoSteps}");
        AoLightingCb.IsChecked = s.AoLighting;
        SelectTag(CabProbeCb, s.CabProbe.ToString(Invariant));
        CabProbeIntervalNud.Value = s.CabProbeInterval;
        CabProbeBounceSlider.Value = s.CabProbeBounce;
        CabProbeReflectionsCb.IsChecked = s.CabProbeReflections;

        SelectTag(SsrCb, s.Ssr.ToString(Invariant));
        RayTracingCb.IsChecked = s.RayTracing;
        RayTracingRangeSlider.Value = s.RayTracingRange;

        VolumetricFogCb.IsChecked = s.VolumetricFog;
        SelectTag(FogQualityCb, s.FogQuality.ToString(Invariant));
        SelectTag(CloudsCb, s.Clouds.ToString(Invariant));
        CloudShadowsCb.IsChecked = s.CloudShadows;

        BloomCb.IsChecked = s.Bloom;
        AutoExposureCb.IsChecked = s.AutoExposure;

        GrassCb.IsChecked = s.Grass;
        GrassDistanceSlider.Value = s.GrassDistance;
        GrassDensitySlider.Value = s.GrassDensity;
        string particles = s.PrecipitationParticles.ToString(Invariant);
        SelectOrAddTag(PrecipitationCb, particles, particles);
    }

    private void CaptureGraphics(SimulatorSettings s)
    {
        s.RenderEngine = Math.Max(0, RenderEngineCb.SelectedIndex);
        s.VulkanDevice = VulkanDeviceTb.Text?.Trim() ?? "";
        s.AntiAliasing = AntiAliasingFromTag(SelectedTag(AntiAliasingCb));
        s.DlssPreset = SelectedTag(DlssPresetCb) ?? "default";
        s.FrameGeneration = TagInt(FrameGenerationCb, 1);
        s.Reflex = TagInt(ReflexCb, 0);

        s.ShadowSdsm = IsChecked(ShadowSdsmCb);
        s.LightsClustered = IsChecked(LightsClusteredCb);
        s.LightShadows = TagInt(LightShadowsCb, 2);
        s.ContactShadows = TagInt(ContactShadowsCb, 0);
        s.AmbientOcclusion = TagInt(AmbientOcclusionCb, 2);
        var ao = (SelectedTag(AoQualityCb) ?? "3 4").Split(' ');
        s.AoSlices = ao.Length > 0 && int.TryParse(ao[0], NumberStyles.Integer, Invariant, out int slices) ? slices : 3;
        s.AoSteps = ao.Length > 1 && int.TryParse(ao[1], NumberStyles.Integer, Invariant, out int steps) ? steps : 4;
        s.AoLighting = IsChecked(AoLightingCb);
        s.CabProbe = TagInt(CabProbeCb, 2);
        s.CabProbeInterval = (int)(CabProbeIntervalNud.Value ?? 4);
        s.CabProbeBounce = CabProbeBounceSlider.Value;
        s.CabProbeReflections = IsChecked(CabProbeReflectionsCb);

        s.Ssr = TagInt(SsrCb, 2);
        s.RayTracing = IsChecked(RayTracingCb);
        s.RayTracingRange = (int)RayTracingRangeSlider.Value;

        s.VolumetricFog = IsChecked(VolumetricFogCb);
        s.FogQuality = TagInt(FogQualityCb, 2);
        s.Clouds = TagInt(CloudsCb, 2);
        s.CloudShadows = IsChecked(CloudShadowsCb);

        s.Bloom = IsChecked(BloomCb);
        s.AutoExposure = IsChecked(AutoExposureCb);

        s.Grass = IsChecked(GrassCb);
        s.GrassDistance = (int)GrassDistanceSlider.Value;
        s.GrassDensity = GrassDensitySlider.Value;
        s.PrecipitationParticles = TagInt(PrecipitationCb, 65536);
    }

    private void OnBackendChanged()
    {
        if (_loading || _updatingGraphics)
            return;

        _updatingGraphics = true;
        try
        {
            if (!SettingReach.IsVulkan(SelectedRenderer()))
            {
                var s = Trial();
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
    /// treats them, and says why something is off.
    /// </summary>
    private void UpdateGraphicsState()
    {
        if (!_built || _loading || _updatingGraphics)
            return;

        _updatingGraphics = true;
        try
        {
            var s = Trial();
            bool vulkan = GraphicsRules.IsVulkan(s);
            bool dlssAllowed = GraphicsRules.DlssAllowed(s, DlssPlatform);

            foreach (var item in AntiAliasingCb.Items.OfType<ComboBoxItem>())
                if (item.Tag is "dlss" or "dlaa")
                    item.IsEnabled = dlssAllowed;
            if (GraphicsRules.IsDlss(s.AntiAliasing) && !dlssAllowed)
                SelectTag(AntiAliasingCb, "taa");

            s = Trial();
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

            s = Trial();
            BufferScaleRow.IsEnabled = GraphicsRules.BufferScaleUsed(s, DlssPlatform);
            RayTracingRangeRow.IsEnabled = vulkan && s.RayTracing;
            DynamicLightsRow.IsEnabled = GraphicsRules.DynamicLightsUsed(s);
            AoQualityRow.IsEnabled = s.AmbientOcclusion > 0;
            AoLightingRow.IsEnabled = GraphicsRules.AoLightingUsed(s);
            bool probe = GraphicsRules.CabProbeUsed(s);
            CabProbeIntervalRow.IsEnabled = probe;
            CabProbeReflectionsRow.IsEnabled = probe;
            CabProbeBounceRow.IsEnabled = GraphicsRules.CabProbeLights(s);
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
            SetNote(RenderEngineNote, App.Loc[vulkan ? "REVulkanNote" : "REOpenGLNote"]);
            SetNote(AntiAliasingNote, AntiAliasingRemark(s, dlss));
            SetNote(FrameGenerationNote, FrameGenerationRemark(s, dlss));
            SetNote(ReflexNote, GraphicsRules.ReflexForced(s, DlssPlatform) ? App.Loc["ReflexForcedNote"] : "");
            SetNote(AoLightingNote, AoLightingRemark(s));
            SetNote(CabProbeNote, GraphicsRules.CabProbeUsed(s) && !GraphicsRules.CabProbeLights(s) ? App.Loc["CabProbeReflectionsOnlyNote"] : "");
            SetNote(SkipPipelineNote, GraphicsRules.TemporalWithoutPipeline(s) ? App.Loc["TemporalNeedsPipelineNote"] : "");
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

    private static string AoLightingRemark(SimulatorSettings s)
    {
        if (!GraphicsRules.AoLightingUsed(s) || !s.AoLighting)
            return "";
        if (s.SkipPipeline)
            return App.Loc["AoNeedsPipelineNote"];
        return GraphicsRules.AoLightingAddsDepthPrepass(s) ? App.Loc["AoLightingPrepassNote"] : "";
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
        // user's own eu07.ini is not written.
        CaptureInto(AppServices.Current.Settings);
        if (!AppServices.Current.SettingsStore.ExportTo(path))
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
