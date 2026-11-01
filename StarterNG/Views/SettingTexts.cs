using System;
using System.Collections.Generic;
using System.Globalization;
using StarterNG.Domain.Settings;

namespace StarterNG.Views;

/// <summary>The captions of the settings' rows and the words for their values.</summary>
internal static class SettingTexts
{
    public static string[] QualityWords => new[]
    {
        App.Loc["QualityVeryLow"], App.Loc["QualityLow"], App.Loc["QualityNormal"],
        App.Loc["QualityHigh"], App.Loc["QualityVeryHigh"]
    };

    /// <summary>The words of a slider that goes from 1 to 4, which has no middle.</summary>
    public static string Fidelity(int level)
    {
        var q = QualityWords;
        string[] four = { q[0], q[1], q[3], q[4] };
        return four[Math.Clamp(level, 1, 4) - 1];
    }

    public static string Anisotropy(double value) => "x" + value.ToString("0.#", CultureInfo.CurrentCulture);

    public static string Multisampling(int level) => level <= 0 ? App.Loc["MsaaNone"] : "x" + (1 << level);

    public static string TwoDecimals(double value) => value.ToString("0.00", CultureInfo.CurrentCulture);

    public static string ShadowMapWord(int pixels) => pixels switch
    {
        <= 512 => App.Loc["QualityVeryLow"],
        <= 1024 => App.Loc["QualityLow"],
        <= 2048 => App.Loc["QualityModerate"],
        <= 4096 => App.Loc["QualityHigh"],
        _ => App.Loc["QualityVeryHigh"]
    };

    public static string ShadowRangeWord(int metres) => metres switch
    {
        <= 100 => App.Loc["RangeLow"],
        <= 150 => App.Loc["RangeModerate"],
        <= 250 => App.Loc["RangeHigh"],
        _ => App.Loc["RangeVeryHigh"]
    };

    public static string CabShadowRangeWord(int metres) => metres switch
    {
        <= 10 => App.Loc["RangeVeryLow"],
        <= 20 => App.Loc["RangeLow"],
        <= 30 => App.Loc["RangeStandard"],
        <= 50 => App.Loc["RangeHigh"],
        _ => App.Loc["RangeVeryHigh"]
    };

    /// <summary>A rate below one a second reads better as the time between updates.</summary>
    public static string ReflectionsInterval(double perSecond)
    {
        if (perSecond >= 1)
            return string.Format(App.Loc["TimesPerSecond"], perSecond.ToString("0.##", CultureInfo.CurrentCulture));
        double seconds = 1 / perSecond;
        return seconds >= 60
            ? string.Format(App.Loc["EveryMinutes"], (seconds / 60).ToString("0.#", CultureInfo.CurrentCulture))
            : string.Format(App.Loc["EverySeconds"], seconds.ToString("0.#", CultureInfo.CurrentCulture));
    }

    /// <summary>What a level sets, in the words and the units of the rows it sets.</summary>
    public static string PresetSummary(GraphicsPreset p)
    {
        var lines = new List<string>();
        void Add(string label, string value) => lines.Add(App.Loc[label] + ": " + value);
        string YesNo(bool on) => App.Loc[on ? "Yes" : "No"];
        string Whole(double value) => value.ToString("0.#", CultureInfo.CurrentCulture);

        Add("MaxTexResolution", p.TextureSize + " px");
        Add("MaxTexCabResolution", p.CabTextureSize + " px");
        Add("TexFilteringQuality", Anisotropy(p.Anisotropy));
        Add("Multisampling", Multisampling(p.Multisampling));
        Add("DynamicLights", p.DynamicLights.ToString(CultureInfo.CurrentCulture));
        Add("SmokeParticlesMultiplier", Fidelity(p.SmokeFidelity));
        Add("RenderShadows", YesNo(p.Shadows));
        if (p.Shadows)
        {
            Add("ShadowsResolution", p.ShadowMap + " px");
            Add("ShaderRange", SimulatorSettings.ShadowReach(p.ShadowRange, p.ShadowMap, legacyRenderer: false) + " m");
            Add("ShadowDisplay", App.Loc[p.ShadowRank switch { 1 => "CSROnlyImportant", 2 => "CSRLimited", _ => "CSRAll" }]);
        }
        Add("ReflectionsCubeMap", YesNo(p.EnvMap));
        if (p.EnvMap)
        {
            Add("ReflectionsDetails", App.Loc[p.ReflectionsFidelity switch { 0 => "RDTerrain", 1 => "RDTerrainModels", _ => "RDTerrainModelsVehicles" }]);
            Add("ReflectionsFramerate", ReflectionsInterval(p.ReflectionsRate));
        }
        Add("AdditionalShadersEffects", YesNo(p.ExtraEffects));
        Add("TrackCurvesQuality", Fidelity(p.SplineFidelity));
        Add("Ini.basedrawrange", Whole(p.DrawRange) + " m");
        Add("Ini.heightbase", "×" + p.DistanceFactor.ToString("0.##", CultureInfo.CurrentCulture));
        Add("BufforResolution", p.BufferScale + "%");
        Add("Ini." + GraphicsPreset.SleeperKey, Whole(p.SleeperDistance) + " m");
        Add("RenderRange", "×" + Whole(p.DrawRangeFactor));
        Add("Ini." + GraphicsPreset.ReflectedInstancesKey, Whole(p.ReflectedInstances) + " m");
        Add("Ini." + GraphicsPreset.ReflectedVehiclesKey, Whole(p.ReflectedVehicles) + " m");
        Add("CabShadowsRange", p.CabShadowsRange + " m");

        if (p.Extras is { } extras)
        {
            Add("MotionBlur", YesNo(extras.MotionBlur));
            Add("ChromaticAberration", YesNo(extras.ChromaticAberration));
            Add("SmokeDisplay", YesNo(extras.Smoke));
            Add("PyScreenPriority", App.Loc[extras.PythonUpdateMs <= 200 ? "PyPriorityNormal" : "PyPriorityLowest"]);
        }
        if (p.Top is { } top)
        {
            lines.Insert(0, App.Loc["PresetUltraNote"]);
            Add("ShadowAngleLimit", TwoDecimals(top.ShadowAngleLimit));
            Add("Ini." + GraphicsTop.TargetFpsKey, YesNo(top.TargetFps > 0));
            Add("Ini." + GraphicsTop.TextureReleaseKey, YesNo(top.TextureRelease > 0));
            Add("CompressTextures", YesNo(top.CompressTextures));
            Add("IgnoreIrrelevantTrains", YesNo(top.SkipDecor));
        }
        return string.Join("\n", lines);
    }
}
