using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StarterNG.Domain.Settings;

/// <summary>
/// What only the two ends of the scale touch: the effects left to taste at every other
/// level and how often the screens refresh. The lowest level gives them up, the highest
/// takes the most there is.
/// </summary>
/// <param name="PythonUpdateMs">python.updatetime, one of the steps of its list.</param>
public sealed record GraphicsExtras(
    bool MotionBlur,
    bool ChromaticAberration,
    bool Smoke,
    int PythonUpdateMs)
{
    /// <summary>The eu07.ini keys these values live in.</summary>
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        "gfx.postfx.motionblur.enabled", "gfx.postfx.chromaticaberration.enabled",
        "gfx.smoke", "python.updatetime"
    };

    /// <summary>The simulator's own, for leaving a level chosen before this run: what was there earlier is gone.</summary>
    public static readonly GraphicsExtras None = new(true, true, true, 200);

    public static GraphicsExtras Of(SimulatorSettings s) =>
        new(s.MotionBlur, s.ChromaticAberration, s.Smoke, s.PythonScreenUpdateRate);

    public void ApplyTo(SimulatorSettings s)
    {
        s.MotionBlur = MotionBlur;
        s.ChromaticAberration = ChromaticAberration;
        s.Smoke = Smoke;
        s.PythonScreenUpdateRate = PythonUpdateMs;
    }
}

/// <summary>
/// What only Ultra sets, switches and limits with no scale to climb. Not every best is the
/// top of a scale: shadows are longest with the limit at its bottom, the regulator and the
/// release of textures would only take away, off.
/// Left to the player, picture unchanged: separate windows for the screens
/// (python.displaywindows, python.vsync), texture streaming, the frame pacing.
/// </summary>
/// <param name="ShadowAngleLimit">gfx.shadow.angle.min: how low the sun counts for shadows, so how long they
/// get: up to 5× the height of what casts them at 0.2, no longer than it at 1 (opengl33renderer.cpp).</param>
/// <param name="TargetFps">targetfps: above 0 the simulator moves the distance factor by itself.</param>
/// <param name="ScreenWindowFps">python.fpslimit: read only by the screens' separate windows
/// (pythonscreenviewer.cpp), it does nothing without them.</param>
/// <param name="TextureRelease">gfx.textures.releasedistance: above 0 textures of far vehicles leave the card.</param>
/// <param name="CompressTextures">compresstex: on saves card memory at a cost to the picture.</param>
/// <param name="SkipDecor">starter.ignoreirrelevant: on leaves decorative consists out of the scenario.</param>
public sealed record GraphicsTop(
    double ShadowAngleLimit,
    double TargetFps,
    double ScreenWindowFps,
    double TextureRelease,
    bool Screens,
    bool ScreenMipmaps,
    bool CompressTextures,
    bool SkipDecor,
    bool Sky,
    bool Cab,
    bool AirCouplers,
    bool SmoothTraction)
{
    public const string TargetFpsKey = "targetfps";
    public const string ScreenWindowFpsKey = "python.fpslimit";
    public const string TextureReleaseKey = "gfx.textures.releasedistance";
    public const string SkyKey = "skyenabled";
    public const string CabKey = "rendercab";
    public const string AirCouplersKey = "newaircouplers";
    public const string SmoothTractionKey = "smoothtraction";

    /// <summary>Those of its keys shown as rows of <see cref="SimulatorKeys"/>.</summary>
    public static readonly IReadOnlyList<string> RowKeys = new[]
    {
        TargetFpsKey, ScreenWindowFpsKey, TextureReleaseKey, SkyKey, CabKey, AirCouplersKey, SmoothTractionKey
    };

    /// <summary>The eu07.ini keys these values live in.</summary>
    public static readonly IReadOnlyList<string> Keys = RowKeys.Concat(new[]
    {
        "gfx.shadow.angle.min", "python.enabled", "python.mipmaps", "compresstex", "starter.ignoreirrelevant"
    }).ToList();

    /// <summary>What they are with nothing set, as <see cref="GraphicsExtras.None"/>.</summary>
    public static readonly GraphicsTop None =
        new(0.2, 0, 100, 0, true, true, true, false, true, true, true, true);

    public static GraphicsTop Of(SimulatorSettings s) =>
        new(s.ShadowAngleLimit, GraphicsPreset.NumberOf(s, TargetFpsKey),
            GraphicsPreset.NumberOf(s, ScreenWindowFpsKey), GraphicsPreset.NumberOf(s, TextureReleaseKey),
            s.PythonScreens, s.ScreenMipmaps, s.CompressTextures, s.IgnoreIrrelevantTrains,
            GraphicsPreset.ToggleOf(s, SkyKey), GraphicsPreset.ToggleOf(s, CabKey),
            GraphicsPreset.ToggleOf(s, AirCouplersKey), GraphicsPreset.ToggleOf(s, SmoothTractionKey));

    public void ApplyTo(SimulatorSettings s)
    {
        void Number(string key, double value) => s.Extra[key] = value.ToString(CultureInfo.InvariantCulture);
        void Toggle(string key, bool value) => s.Extra[key] = SimulatorKey.FormatToggle(value);

        s.ShadowAngleLimit = ShadowAngleLimit;
        Number(TargetFpsKey, TargetFps);
        Number(ScreenWindowFpsKey, ScreenWindowFps);
        Number(TextureReleaseKey, TextureRelease);
        s.PythonScreens = Screens;
        s.ScreenMipmaps = ScreenMipmaps;
        s.CompressTextures = CompressTextures;
        s.IgnoreIrrelevantTrains = SkipDecor;
        Toggle(SkyKey, Sky);
        Toggle(CabKey, Cab);
        Toggle(AirCouplersKey, AirCouplers);
        Toggle(SmoothTractionKey, SmoothTraction);
    }
}

/// <summary>
/// What only the simulators with the OpenGL 4.6 and Vulkan 1.4 renderers read, the others pass it
/// over. High is what they do with nothing set. Left to the player: anti-aliasing by TAA or DLSS,
/// frame generation, Reflex, the choice of the card, automatic exposure and the fitted cascades.
/// </summary>
/// <param name="CabProbeInterval">gfx.cabprobe.interval, in frames: the lower the dearer.</param>
/// <param name="RayTracing">gfx.raytracing: the Vulkan renderer's alone, OpenGL passes it over.</param>
public sealed record GraphicsNewRenderer(
    string LampShadows,
    string ContactShadows,
    string Occlusion,
    string OcclusionQuality,
    bool OcclusionLighting,
    string CabProbe,
    double CabProbeInterval,
    bool CabProbeReflections,
    string ScreenReflections,
    bool RayTracing,
    string Fog,
    string FogQuality,
    string Clouds,
    bool CloudShadows,
    bool Grass,
    double GrassDistance,
    double GrassFade,
    double GrassDensity,
    double Precipitation,
    bool Bloom)
{
    private const string LampShadowsKey = "gfx.lights.shadows";
    private const string ContactShadowsKey = "gfx.contactshadows";
    private const string OcclusionKey = "gfx.postfx.ao";
    private const string OcclusionQualityKey = "gfx.postfx.ao.quality";
    private const string OcclusionLightingKey = "gfx.postfx.ao.lighting";
    private const string CabProbeKey = "gfx.cabprobe";
    private const string CabProbeIntervalKey = "gfx.cabprobe.interval";
    private const string CabProbeReflectionsKey = "gfx.cabprobe.reflections";
    private const string ScreenReflectionsKey = "gfx.ssr";
    private const string RayTracingKey = "gfx.raytracing";
    private const string FogKey = "gfx.fog";
    private const string FogQualityKey = "gfx.fog.quality";
    private const string CloudsKey = "gfx.clouds";
    private const string CloudShadowsKey = "gfx.clouds.shadows";
    private const string GrassKey = "gfx.grass";
    private const string GrassDistanceKey = "gfx.grass.distance";
    private const string GrassFadeKey = "gfx.grass.fadedistance";
    private const string GrassDensityKey = "gfx.grass.density";
    private const string PrecipitationKey = "gfx.precipitation.particles";
    private const string BloomKey = "gfx.postfx.bloom";

    /// <summary>The eu07.ini keys these values live in, all of them rows of <see cref="SimulatorKeys"/>.</summary>
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        LampShadowsKey, ContactShadowsKey, OcclusionKey, OcclusionQualityKey, OcclusionLightingKey,
        CabProbeKey, CabProbeIntervalKey, CabProbeReflectionsKey, ScreenReflectionsKey, RayTracingKey,
        FogKey, FogQualityKey, CloudsKey, CloudShadowsKey,
        GrassKey, GrassDistanceKey, GrassFadeKey, GrassDensityKey, PrecipitationKey, BloomKey
    };

    public static GraphicsNewRenderer Of(SimulatorSettings s) =>
        new(WordOf(s, LampShadowsKey), WordOf(s, ContactShadowsKey), WordOf(s, OcclusionKey), WordOf(s, OcclusionQualityKey),
            GraphicsPreset.ToggleOf(s, OcclusionLightingKey), WordOf(s, CabProbeKey), GraphicsPreset.NumberOf(s, CabProbeIntervalKey),
            GraphicsPreset.ToggleOf(s, CabProbeReflectionsKey), WordOf(s, ScreenReflectionsKey), GraphicsPreset.ToggleOf(s, RayTracingKey),
            WordOf(s, FogKey), WordOf(s, FogQualityKey), WordOf(s, CloudsKey), GraphicsPreset.ToggleOf(s, CloudShadowsKey),
            GraphicsPreset.ToggleOf(s, GrassKey), GraphicsPreset.NumberOf(s, GrassDistanceKey), GraphicsPreset.NumberOf(s, GrassFadeKey),
            GraphicsPreset.NumberOf(s, GrassDensityKey), GraphicsPreset.NumberOf(s, PrecipitationKey), GraphicsPreset.ToggleOf(s, BloomKey));

    public void ApplyTo(SimulatorSettings s)
    {
        void Number(string key, double value) => s.Extra[key] = value.ToString(CultureInfo.InvariantCulture);
        void Toggle(string key, bool value) => s.Extra[key] = SimulatorKey.FormatToggle(value);

        s.Extra[LampShadowsKey] = LampShadows;
        s.Extra[ContactShadowsKey] = ContactShadows;
        s.Extra[OcclusionKey] = Occlusion;
        s.Extra[OcclusionQualityKey] = OcclusionQuality;
        Toggle(OcclusionLightingKey, OcclusionLighting);
        s.Extra[CabProbeKey] = CabProbe;
        Number(CabProbeIntervalKey, CabProbeInterval);
        Toggle(CabProbeReflectionsKey, CabProbeReflections);
        s.Extra[ScreenReflectionsKey] = ScreenReflections;
        Toggle(RayTracingKey, RayTracing);
        s.Extra[FogKey] = Fog;
        s.Extra[FogQualityKey] = FogQuality;
        s.Extra[CloudsKey] = Clouds;
        Toggle(CloudShadowsKey, CloudShadows);
        Toggle(GrassKey, Grass);
        Number(GrassDistanceKey, GrassDistance);
        Number(GrassFadeKey, GrassFade);
        Number(GrassDensityKey, GrassDensity);
        Number(PrecipitationKey, Precipitation);
        Toggle(BloomKey, Bloom);
    }

    private static string WordOf(SimulatorSettings s, string name)
    {
        var key = SimulatorKeys.Named(name);
        return key.ParseChoice(key.RawIn(s));
    }
}

/// <summary>
/// A level of detail set in one go: what weighs on the graphics card. Every scale climbs in
/// even steps from <see cref="Minimal"/> to <see cref="Ultra"/>, as far as it has steps for six
/// levels. The screen (resolution, field of view), the frame pacing and the effects that are a
/// matter of taste - motion blur, chromatic aberration - stay as they are, except at the
/// two ends of the scale, which have <see cref="Extras"/>.
/// </summary>
/// <param name="NameKey">Its name's translation key.</param>
/// <param name="ReflectionsRate">gfx.reflections.framerate, in updates a second.</param>
/// <param name="DrawRange">basedrawrange, in metres. Below 2000 it changes nothing on screen: the renderer
/// keeps at least that for signals and lights (opengl33renderer.cpp).</param>
/// <param name="DistanceFactor">heightbase: the only thing that draws less than those 2 km, and draws
/// shapes, wires and sections nearer. A regulating targetfps moves it from there.</param>
/// <param name="BufferScale">starter.bufferscale, in percent. Above the screen over High: on a 4K one
/// Ultra is a picture of 8K, multisampled on top.</param>
/// <param name="DrawRangeFactor">gfx.drawrange.factor.max: how far the regulator may stretch the draw range.</param>
/// <param name="CabShadowsRange">gfx.shadows.cab.range, in metres, read only by the legacy renderer.</param>
public sealed record GraphicsPreset(
    string NameKey,
    int TextureSize,
    int CabTextureSize,
    double Anisotropy,
    int Multisampling,
    int DynamicLights,
    int SmokeFidelity,
    bool Shadows,
    int ShadowMap,
    int ShadowRange,
    int ShadowRank,
    bool EnvMap,
    int ReflectionsFidelity,
    double ReflectionsRate,
    bool ExtraEffects,
    int SplineFidelity,
    double DrawRange,
    double DistanceFactor,
    int BufferScale,
    double SleeperDistance,
    double DrawRangeFactor,
    double ReflectedInstances,
    double ReflectedVehicles,
    int CabShadowsRange,
    GraphicsNewRenderer NewRenderer,
    GraphicsExtras? Extras = null,
    GraphicsTop? Top = null)
{
    public const string DrawRangeKey = "basedrawrange";
    public const string DistanceFactorKey = "heightbase";
    public const string SleeperKey = "sleeperdistance";
    public const string ReflectedInstancesKey = "gfx.reflections.range_instances";
    public const string ReflectedVehiclesKey = "gfx.reflections.range_vehicles";

    /// <summary>Those of its keys shown as rows of <see cref="SimulatorKeys"/>.</summary>
    public static readonly IReadOnlyList<string> RowKeys = new[]
    {
        DrawRangeKey, DistanceFactorKey, SleeperKey, ReflectedInstancesKey, ReflectedVehiclesKey
    }.Concat(GraphicsNewRenderer.Keys).ToList();

    /// <summary>The eu07.ini keys a level sets.</summary>
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        "maxtexturesize", "maxcabtexturesize", "anisotropicfiltering", "multisampling", "dynamiclights",
        "gfx.smoke.fidelity", "shadows", "gfx.shadowmap.enabled", "shadowtune", "gfx.shadow.rank.cutoff",
        "gfx.envmap.enabled", "gfx.reflections.fidelity", "gfx.reflections.framerate", "gfx.extraeffects",
        "splinefidelity", DrawRangeKey, DistanceFactorKey, "starter.bufferscale", SleeperKey,
        "gfx.drawrange.factor.max", ReflectedInstancesKey, ReflectedVehiclesKey, "gfx.shadows.cab.range"
    }.Concat(GraphicsNewRenderer.Keys).ToList();

    /// <summary>The last resort: no shadows, no reflections, no effects, no smoke, a softer picture.</summary>
    public static readonly GraphicsPreset Minimal = new(
        "PresetMinimal", TextureSize: 512, CabTextureSize: 512, Anisotropy: 1, Multisampling: 0,
        DynamicLights: 1, SmokeFidelity: 1, Shadows: false, ShadowMap: 512, ShadowRange: 75, ShadowRank: 1,
        EnvMap: false, ReflectionsFidelity: 0, ReflectionsRate: 1 / 300.0, ExtraEffects: false,
        SplineFidelity: 1, DrawRange: 2000, DistanceFactor: 0.6, BufferScale: 70,
        SleeperDistance: 100, DrawRangeFactor: 1, ReflectedInstances: 250, ReflectedVehicles: 50, CabShadowsRange: 0,
        NewRenderer: new GraphicsNewRenderer(LampShadows: "no", ContactShadows: "no", Occlusion: "no", OcclusionQuality: "3 4",
            OcclusionLighting: false, CabProbe: "no", CabProbeInterval: 8, CabProbeReflections: false, ScreenReflections: "no",
            RayTracing: false, Fog: "analytic", FogQuality: "low", Clouds: "no", CloudShadows: false, Grass: false,
            GrassDistance: 30, GrassFade: 40, GrassDensity: 0.25, Precipitation: 16384, Bloom: false),
        Extras: new GraphicsExtras(MotionBlur: false, ChromaticAberration: false, Smoke: false, PythonUpdateMs: 800));

    public static readonly GraphicsPreset Low = new(
        "PresetLow", TextureSize: 1024, CabTextureSize: 1024, Anisotropy: 2, Multisampling: 0,
        DynamicLights: 3, SmokeFidelity: 1, Shadows: true, ShadowMap: 1024, ShadowRange: 125, ShadowRank: 2,
        EnvMap: true, ReflectionsFidelity: 0, ReflectionsRate: 1 / 60.0, ExtraEffects: true,
        SplineFidelity: 1, DrawRange: 2000, DistanceFactor: 0.7, BufferScale: 80,
        SleeperDistance: 150, DrawRangeFactor: 1.5, ReflectedInstances: 400, ReflectedVehicles: 100, CabShadowsRange: 0,
        NewRenderer: new GraphicsNewRenderer(LampShadows: "low", ContactShadows: "no", Occlusion: "ssao", OcclusionQuality: "3 4",
            OcclusionLighting: false, CabProbe: "no", CabProbeInterval: 8, CabProbeReflections: false, ScreenReflections: "low",
            RayTracing: false, Fog: "volumetric", FogQuality: "low", Clouds: "low", CloudShadows: false, Grass: true,
            GrassDistance: 40, GrassFade: 50, GrassDensity: 0.5, Precipitation: 32768, Bloom: true));

    public static readonly GraphicsPreset Medium = new(
        "PresetMedium", TextureSize: 2048, CabTextureSize: 2048, Anisotropy: 4, Multisampling: 1,
        DynamicLights: 5, SmokeFidelity: 2, Shadows: true, ShadowMap: 1024, ShadowRange: 175, ShadowRank: 2,
        EnvMap: true, ReflectionsFidelity: 1, ReflectionsRate: 1 / 30.0, ExtraEffects: true,
        SplineFidelity: 2, DrawRange: 2250, DistanceFactor: 0.85, BufferScale: 90,
        SleeperDistance: 200, DrawRangeFactor: 2, ReflectedInstances: 550, ReflectedVehicles: 150, CabShadowsRange: 20,
        NewRenderer: new GraphicsNewRenderer(LampShadows: "low", ContactShadows: "no", Occlusion: "ssao", OcclusionQuality: "3 4",
            OcclusionLighting: false, CabProbe: "low", CabProbeInterval: 8, CabProbeReflections: true, ScreenReflections: "low",
            RayTracing: false, Fog: "volumetric", FogQuality: "low", Clouds: "low", CloudShadows: true, Grass: true,
            GrassDistance: 50, GrassFade: 60, GrassDensity: 0.75, Precipitation: 49152, Bloom: true));

    /// <summary>
    /// Where a player with no settings of their own starts. The simulator's own values (Globals.h),
    /// but for smoke, track curves, reflections and cab shadows: theirs are the bottom of a scale
    /// with levels to go under this one.
    /// </summary>
    public static readonly GraphicsPreset High = new(
        "PresetHigh", TextureSize: 4096, CabTextureSize: 4096, Anisotropy: 8, Multisampling: 2,
        DynamicLights: 7, SmokeFidelity: 2, Shadows: true, ShadowMap: 2048, ShadowRange: 250, ShadowRank: 3,
        EnvMap: true, ReflectionsFidelity: 1, ReflectionsRate: 0.1, ExtraEffects: true,
        SplineFidelity: 2, DrawRange: 2500, DistanceFactor: 1, BufferScale: 100,
        SleeperDistance: 250, DrawRangeFactor: 3, ReflectedInstances: 750, ReflectedVehicles: 250, CabShadowsRange: 40,
        NewRenderer: new GraphicsNewRenderer(LampShadows: "medium", ContactShadows: "no", Occlusion: "gtao", OcclusionQuality: "3 4",
            OcclusionLighting: true, CabProbe: "medium", CabProbeInterval: 4, CabProbeReflections: true, ScreenReflections: "medium",
            RayTracing: false, Fog: "volumetric", FogQuality: "medium", Clouds: "medium", CloudShadows: true, Grass: true,
            GrassDistance: 60, GrassFade: 70, GrassDensity: 1, Precipitation: 65536, Bloom: true));

    /// <summary>For a strong card. A map of 4096 doubles the cascades, so the range here reaches 800 m.</summary>
    public static readonly GraphicsPreset VeryHigh = new(
        "PresetVeryHigh", TextureSize: 8192, CabTextureSize: 8192, Anisotropy: 16, Multisampling: 3,
        DynamicLights: 7, SmokeFidelity: 3, Shadows: true, ShadowMap: 4096, ShadowRange: 400, ShadowRank: 3,
        EnvMap: true, ReflectionsFidelity: 2, ReflectionsRate: 2, ExtraEffects: true,
        SplineFidelity: 3, DrawRange: 5000, DistanceFactor: 2, BufferScale: 150,
        SleeperDistance: 2000, DrawRangeFactor: 3, ReflectedInstances: 1500, ReflectedVehicles: 1000, CabShadowsRange: 70,
        NewRenderer: new GraphicsNewRenderer(LampShadows: "high", ContactShadows: "medium", Occlusion: "gtao", OcclusionQuality: "4 6",
            OcclusionLighting: true, CabProbe: "high", CabProbeInterval: 3, CabProbeReflections: true, ScreenReflections: "high",
            RayTracing: false, Fog: "volumetric", FogQuality: "high", Clouds: "high", CloudShadows: true, Grass: true,
            GrassDistance: 120, GrassFade: 140, GrassDensity: 1.5, Precipitation: 98304, Bloom: true));

    /// <summary>
    /// Every value at the top of what the screen offers. Not the best picture throughout: a map
    /// of 8192 stretches the cascades fourfold, so shadows reach 2400 m but are coarser up close
    /// than at <see cref="VeryHigh"/>.
    /// </summary>
    public static readonly GraphicsPreset Ultra = new(
        "PresetUltra", TextureSize: 8192, CabTextureSize: 8192, Anisotropy: 16, Multisampling: 3,
        DynamicLights: 7, SmokeFidelity: 4, Shadows: true, ShadowMap: 8192, ShadowRange: 600, ShadowRank: 3,
        EnvMap: true, ReflectionsFidelity: 2, ReflectionsRate: 60, ExtraEffects: true,
        SplineFidelity: 4, DrawRange: 10000, DistanceFactor: 3, BufferScale: 200,
        SleeperDistance: 5000, DrawRangeFactor: 3, ReflectedInstances: 3000, ReflectedVehicles: 2000, CabShadowsRange: 100,
        NewRenderer: new GraphicsNewRenderer(LampShadows: "high", ContactShadows: "high", Occlusion: "gtao", OcclusionQuality: "6 8",
            OcclusionLighting: true, CabProbe: "high", CabProbeInterval: 2, CabProbeReflections: true, ScreenReflections: "high",
            RayTracing: true, Fog: "volumetric", FogQuality: "ultra", Clouds: "ultra", CloudShadows: true, Grass: true,
            GrassDistance: 300, GrassFade: 350, GrassDensity: 2, Precipitation: 131072, Bloom: true),
        Extras: new GraphicsExtras(MotionBlur: true, ChromaticAberration: true, Smoke: true, PythonUpdateMs: 200),
        Top: new GraphicsTop(ShadowAngleLimit: 0.2, TargetFps: 0, ScreenWindowFps: 200, TextureRelease: 0,
                             Screens: true, ScreenMipmaps: true, CompressTextures: false, SkipDecor: false,
                             Sky: true, Cab: true, AirCouplers: true, SmoothTraction: true));

    public static readonly IReadOnlyList<GraphicsPreset> All = new[] { Minimal, Low, Medium, High, VeryHigh, Ultra };

    public void ApplyTo(SimulatorSettings s)
    {
        s.MaxTextureSize = TextureSize;
        s.MaxCabTextureSize = CabTextureSize;
        s.TextureFiltering = Anisotropy;
        s.Multisampling = Multisampling;
        s.DynamicLights = DynamicLights;
        s.SmokeFidelity = SmokeFidelity;
        s.RenderShadows = Shadows;
        s.ShadowMapResolution = ShadowMap;
        s.ShadowProjectionRange = ShadowRange;
        s.ShadowRankCutoff = ShadowRank;
        s.EnvMap = EnvMap;
        s.ReflectionsFidelity = ReflectionsFidelity;
        s.ReflectionsFramerate = ReflectionsRate;
        s.ExtraEffects = ExtraEffects;
        s.SplineFidelity = SplineFidelity;
        s.Extra[DrawRangeKey] = DrawRange.ToString(CultureInfo.InvariantCulture);
        s.Extra[DistanceFactorKey] = DistanceFactor.ToString(CultureInfo.InvariantCulture);
        s.BufferScalePercent = BufferScale;
        s.Extra[SleeperKey] = SleeperDistance.ToString(CultureInfo.InvariantCulture);
        s.DrawRangeFactor = DrawRangeFactor;
        s.Extra[ReflectedInstancesKey] = ReflectedInstances.ToString(CultureInfo.InvariantCulture);
        s.Extra[ReflectedVehiclesKey] = ReflectedVehicles.ToString(CultureInfo.InvariantCulture);
        s.CabShadowsRange = CabShadowsRange;
        NewRenderer.ApplyTo(s);
        Extras?.ApplyTo(s);
        Top?.ApplyTo(s);
    }

    /// <summary>Whether <paramref name="s"/> is at this level throughout.</summary>
    public bool Matches(SimulatorSettings s) =>
        s.MaxTextureSize == TextureSize && s.MaxCabTextureSize == CabTextureSize &&
        Near(s.TextureFiltering, Anisotropy) && s.Multisampling == Multisampling &&
        s.DynamicLights == DynamicLights && s.SmokeFidelity == SmokeFidelity &&
        s.RenderShadows == Shadows && s.ShadowMapResolution == ShadowMap &&
        s.ShadowProjectionRange == ShadowRange && s.ShadowRankCutoff == ShadowRank &&
        s.EnvMap == EnvMap && s.ReflectionsFidelity == ReflectionsFidelity &&
        Near(s.ReflectionsFramerate, ReflectionsRate) && s.ExtraEffects == ExtraEffects &&
        s.SplineFidelity == SplineFidelity && Near(NumberOf(s, DrawRangeKey), DrawRange) &&
        Near(NumberOf(s, DistanceFactorKey), DistanceFactor) &&
        s.BufferScalePercent == BufferScale &&
        Near(NumberOf(s, SleeperKey), SleeperDistance) && Near(s.DrawRangeFactor, DrawRangeFactor) &&
        Near(NumberOf(s, ReflectedInstancesKey), ReflectedInstances) &&
        Near(NumberOf(s, ReflectedVehiclesKey), ReflectedVehicles) &&
        s.CabShadowsRange == CabShadowsRange &&
        GraphicsNewRenderer.Of(s) == NewRenderer &&
        (Extras is null || GraphicsExtras.Of(s) == Extras) &&
        (Top is null || GraphicsTop.Of(s) == Top);

    /// <summary>The level <paramref name="s"/> is at throughout, if any.</summary>
    public static GraphicsPreset? Matching(SimulatorSettings s) => All.FirstOrDefault(preset => preset.Matches(s));

    internal static double NumberOf(SimulatorSettings s, string name)
    {
        var key = SimulatorKeys.Named(name);
        return key.ParseNumber(key.RawIn(s));
    }

    internal static bool ToggleOf(SimulatorSettings s, string name) =>
        SimulatorKey.ParseToggle(SimulatorKeys.Named(name).RawIn(s));

    // As the file rounds it: 1/300 comes back as 0.003333.
    private static bool Near(double value, double target) =>
        Math.Abs(value - target) <= Math.Abs(target) * 2e-4 + 1e-9;
}

/// <summary>
/// A part of the picture with a level of its own, as its section on the screen has it. The rest
/// of a level - the buffer, the frame pacing, the sky and the cab - goes only with the whole.
/// </summary>
/// <param name="Keys">The eu07.ini keys it sets.</param>
public sealed record GraphicsGroup(IReadOnlyList<string> Keys)
{
    public static readonly GraphicsGroup DrawRange = new(new[]
    {
        GraphicsPreset.DrawRangeKey, GraphicsPreset.DistanceFactorKey, "splinefidelity",
        GraphicsPreset.SleeperKey, "gfx.drawrange.factor.max"
    });

    public static readonly GraphicsGroup Textures = new(new[]
    {
        "maxtexturesize", "maxcabtexturesize", "anisotropicfiltering", "multisampling",
        "compresstex", GraphicsTop.TextureReleaseKey
    });

    public static readonly GraphicsGroup Effects = new(new[]
    {
        "dynamiclights", "gfx.smoke.fidelity", "gfx.extraeffects", "gfx.postfx.motionblur.enabled",
        "gfx.postfx.chromaticaberration.enabled", "gfx.smoke"
    });

    public static readonly GraphicsGroup Shadows = new(new[]
    {
        "shadows", "gfx.shadowmap.enabled", "shadowtune", "gfx.shadow.rank.cutoff",
        "gfx.shadow.angle.min", "gfx.shadows.cab.range"
    });

    public static readonly GraphicsGroup Reflections = new(new[]
    {
        "gfx.envmap.enabled", "gfx.reflections.fidelity", "gfx.reflections.framerate",
        GraphicsPreset.ReflectedInstancesKey, GraphicsPreset.ReflectedVehiclesKey
    });

    public static readonly GraphicsGroup NewRenderer = new(GraphicsNewRenderer.Keys);

    public static readonly IReadOnlyList<GraphicsGroup> All = new[] { DrawRange, Textures, Effects, Shadows, Reflections, NewRenderer };
}

/// <summary>What applying a level touched.</summary>
/// <param name="RowKeys">The keys of <see cref="SimulatorKeys"/> rows to show its values in, in that order.</param>
/// <param name="ChosenKeys">The eu07.ini keys it chose as a whole.</param>
public sealed record PresetApplied(IReadOnlyList<string> RowKeys, IReadOnlyList<string> ChosenKeys);

/// <summary>
/// Remembers what was there before an end of the scale, which comes back with any level
/// between. Leaving one chosen before this run, what was there earlier is gone, so the
/// simulator's own values take its place.
/// </summary>
public sealed class GraphicsPresetMemory
{
    private GraphicsExtras? _beforeExtras;

    private GraphicsTop? _beforeTop;

    /// <summary>Puts <paramref name="preset"/> in <paramref name="settings"/>, with what an end of the scale left behind.</summary>
    public PresetApplied Apply(GraphicsPreset preset, SimulatorSettings settings)
    {
        var current = GraphicsPreset.Matching(settings);
        var coming = preset.Extras is not null ? null
            : _beforeExtras ?? (current?.Extras is not null ? GraphicsExtras.None : null);
        _beforeExtras = preset.Extras is not null ? _beforeExtras ?? GraphicsExtras.Of(settings) : null;
        var comingTop = preset.Top is not null ? null
            : _beforeTop ?? (current?.Top is not null ? GraphicsTop.None : null);
        _beforeTop = preset.Top is not null ? _beforeTop ?? GraphicsTop.Of(settings) : null;
        coming?.ApplyTo(settings);
        comingTop?.ApplyTo(settings);
        preset.ApplyTo(settings);

        var rows = new List<string>(GraphicsPreset.RowKeys);
        var chosen = new List<string>(GraphicsPreset.Keys);
        if (preset.Extras is not null || coming is not null)
            chosen.AddRange(GraphicsExtras.Keys);
        if (preset.Top is not null || comingTop is not null)
        {
            chosen.AddRange(GraphicsTop.Keys);
            rows.AddRange(GraphicsTop.RowKeys);
        }
        return new PresetApplied(rows, chosen);
    }
}
