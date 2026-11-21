using System;
using System.Collections.Generic;
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
/// What only Ultra sets, with no scale to climb: the longest shadows, at the bottom of their
/// limit, and the screens drawn. What only the advanced mode shows no level sets.
/// </summary>
/// <param name="ShadowAngleLimit">gfx.shadow.angle.min: how low the sun counts for shadows, so how long they
/// get: up to 5× the height of what casts them at 0.2, no longer than it at 1 (opengl33renderer.cpp).</param>
/// <param name="Screens">python.enabled.</param>
public sealed record GraphicsTop(double ShadowAngleLimit, bool Screens)
{
    /// <summary>The eu07.ini keys these values live in.</summary>
    public static readonly IReadOnlyList<string> Keys = new[] { "gfx.shadow.angle.min", "python.enabled" };

    /// <summary>What they are with nothing set, as <see cref="GraphicsExtras.None"/>.</summary>
    public static readonly GraphicsTop None = new(0.2, true);

    public static GraphicsTop Of(SimulatorSettings s) => new(s.ShadowAngleLimit, s.PythonScreens);

    public void ApplyTo(SimulatorSettings s)
    {
        s.ShadowAngleLimit = ShadowAngleLimit;
        s.PythonScreens = Screens;
    }
}

/// <summary>
/// A level of detail set in one go: what weighs on the graphics card. Every scale climbs in
/// even steps from <see cref="Minimal"/> to <see cref="Ultra"/>, as far as it has steps for six
/// levels. A level sets only what the standard mode shows, and only to what its sliders offer
/// (<see cref="SliderRanges"/>, the old starter's ranges): never past the old starter. The screen
/// (resolution, buffer, field of view), the frame pacing and the effects that are a matter of
/// taste - motion blur, chromatic aberration - stay as they are, except at the two ends of the
/// scale, which have <see cref="Extras"/>.
/// </summary>
/// <param name="NameKey">Its name's translation key.</param>
/// <param name="CabShadowsRange">gfx.shadows.cab.range, in metres, read only by the legacy renderer.</param>
/// <param name="AntiAliasing">multisampling.</param>
/// <param name="ReflectionsRate">gfx.reflections.framerate, in updates a second, one of the old starter's steps.</param>
/// <param name="DrawRangeFactor">gfx.drawrange.factor.max: how far the regulator may stretch the draw range.</param>
public sealed record GraphicsPreset(
    string NameKey,
    int TextureSize,
    int CabTextureSize,
    double Anisotropy,
    AntiAliasingMode AntiAliasing,
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
    double DrawRangeFactor,
    int CabShadowsRange,
    GraphicsExtras? Extras = null,
    GraphicsTop? Top = null)
{
    /// <summary>The eu07.ini keys a level sets.</summary>
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        "maxtexturesize", "maxcabtexturesize", "anisotropicfiltering", "multisampling", "dynamiclights",
        "gfx.smoke.fidelity", "shadows", "gfx.shadowmap.enabled", "shadowtune", "gfx.shadow.rank.cutoff",
        "gfx.envmap.enabled", "gfx.reflections.fidelity", "gfx.reflections.framerate", "gfx.extraeffects",
        "splinefidelity", "gfx.drawrange.factor.max", "gfx.shadows.cab.range"
    };

    /// <summary>The last resort: no shadows, no reflections, no effects, no smoke.</summary>
    public static readonly GraphicsPreset Minimal = new(
        "PresetMinimal", TextureSize: 512, CabTextureSize: 512, Anisotropy: 1, AntiAliasing: AntiAliasingMode.Off,
        DynamicLights: 1, SmokeFidelity: 1, Shadows: false, ShadowMap: 1024, ShadowRange: 75, ShadowRank: 1,
        EnvMap: false, ReflectionsFidelity: 0, ReflectionsRate: 1, ExtraEffects: false,
        SplineFidelity: 1, DrawRangeFactor: 1, CabShadowsRange: 0,
        Extras: new GraphicsExtras(MotionBlur: false, ChromaticAberration: false, Smoke: false, PythonUpdateMs: 800));

    public static readonly GraphicsPreset Low = new(
        "PresetLow", TextureSize: 1024, CabTextureSize: 1024, Anisotropy: 2, AntiAliasing: AntiAliasingMode.Off,
        DynamicLights: 3, SmokeFidelity: 1, Shadows: true, ShadowMap: 1024, ShadowRange: 125, ShadowRank: 2,
        EnvMap: true, ReflectionsFidelity: 0, ReflectionsRate: 1, ExtraEffects: true,
        SplineFidelity: 2, DrawRangeFactor: 1, CabShadowsRange: 0);

    public static readonly GraphicsPreset Medium = new(
        "PresetMedium", TextureSize: 2048, CabTextureSize: 2048, Anisotropy: 4, AntiAliasing: AntiAliasingMode.Msaa2,
        DynamicLights: 5, SmokeFidelity: 2, Shadows: true, ShadowMap: 1024, ShadowRange: 175, ShadowRank: 2,
        EnvMap: true, ReflectionsFidelity: 1, ReflectionsRate: 1, ExtraEffects: true,
        SplineFidelity: 2, DrawRangeFactor: 2, CabShadowsRange: 20);

    /// <summary>
    /// Where a player with no settings of their own starts. The simulator's own values (Globals.h),
    /// but for smoke, track curves, reflections and cab shadows: theirs are the bottom of a scale
    /// with levels to go under this one, or below what the old starter offered.
    /// </summary>
    public static readonly GraphicsPreset High = new(
        "PresetHigh", TextureSize: 4096, CabTextureSize: 4096, Anisotropy: 8, AntiAliasing: AntiAliasingMode.Msaa4,
        DynamicLights: 7, SmokeFidelity: 2, Shadows: true, ShadowMap: 2048, ShadowRange: 250, ShadowRank: 3,
        EnvMap: true, ReflectionsFidelity: 1, ReflectionsRate: 3, ExtraEffects: true,
        SplineFidelity: 2, DrawRangeFactor: 3, CabShadowsRange: 30);

    /// <summary>
    /// For a strong card. A map of 4096 doubles the cascades, so the range here reaches 800 m.
    /// Filtering stays as High's, so the textures tell this level from Ultra.
    /// </summary>
    public static readonly GraphicsPreset VeryHigh = new(
        "PresetVeryHigh", TextureSize: 8192, CabTextureSize: 4096, Anisotropy: 8, AntiAliasing: AntiAliasingMode.Msaa8,
        DynamicLights: 7, SmokeFidelity: 3, Shadows: true, ShadowMap: 4096, ShadowRange: 400, ShadowRank: 3,
        EnvMap: true, ReflectionsFidelity: 2, ReflectionsRate: 10, ExtraEffects: true,
        SplineFidelity: 3, DrawRangeFactor: 3, CabShadowsRange: 50);

    /// <summary>
    /// The top of every slider, which is the most the old starter gave. Not the best picture
    /// throughout: a map of 8192 stretches the cascades fourfold, so shadows reach 1600 m but are
    /// coarser up close than at <see cref="VeryHigh"/>.
    /// </summary>
    public static readonly GraphicsPreset Ultra = new(
        "PresetUltra", TextureSize: 8192, CabTextureSize: 4096, Anisotropy: 16, AntiAliasing: AntiAliasingMode.Msaa8,
        DynamicLights: 7, SmokeFidelity: 4, Shadows: true, ShadowMap: 8192, ShadowRange: 400, ShadowRank: 3,
        EnvMap: true, ReflectionsFidelity: 2, ReflectionsRate: 60, ExtraEffects: true,
        SplineFidelity: 4, DrawRangeFactor: 3, CabShadowsRange: 100,
        Extras: new GraphicsExtras(MotionBlur: true, ChromaticAberration: true, Smoke: true, PythonUpdateMs: 200),
        Top: new GraphicsTop(ShadowAngleLimit: 0.2, Screens: true));

    public static readonly IReadOnlyList<GraphicsPreset> All = new[] { Minimal, Low, Medium, High, VeryHigh, Ultra };

    public void ApplyTo(SimulatorSettings s)
    {
        s.MaxTextureSize = TextureSize;
        s.MaxCabTextureSize = CabTextureSize;
        s.TextureFiltering = Anisotropy;
        s.AntiAliasing = AntiAliasing;
        s.CabShadowsRange = CabShadowsRange;
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
        s.DrawRangeFactor = DrawRangeFactor;
        Extras?.ApplyTo(s);
        Top?.ApplyTo(s);
    }

    /// <summary>Whether <paramref name="s"/> is at this level throughout.</summary>
    public bool Matches(SimulatorSettings s) =>
        s.MaxTextureSize == TextureSize && s.MaxCabTextureSize == CabTextureSize &&
        Near(s.TextureFiltering, Anisotropy) && s.AntiAliasing == AntiAliasing &&
        s.DynamicLights == DynamicLights && s.SmokeFidelity == SmokeFidelity &&
        s.RenderShadows == Shadows && s.ShadowMapResolution == ShadowMap &&
        s.ShadowProjectionRange == ShadowRange && s.ShadowRankCutoff == ShadowRank &&
        s.EnvMap == EnvMap && s.ReflectionsFidelity == ReflectionsFidelity &&
        Near(s.ReflectionsFramerate, ReflectionsRate) && s.ExtraEffects == ExtraEffects &&
        s.SplineFidelity == SplineFidelity && Near(s.DrawRangeFactor, DrawRangeFactor) &&
        s.CabShadowsRange == CabShadowsRange &&
        (Extras is null || GraphicsExtras.Of(s) == Extras) &&
        (Top is null || GraphicsTop.Of(s) == Top);

    /// <summary>The level <paramref name="s"/> is at throughout, if any.</summary>
    public static GraphicsPreset? Matching(SimulatorSettings s) => All.FirstOrDefault(preset => preset.Matches(s));

    internal static double NumberOf(SimulatorSettings s, string name)
    {
        var key = SimulatorKeys.Named(name);
        return key.ParseNumber(key.RawIn(s));
    }

    // As the file rounds it.
    private static bool Near(double value, double target) =>
        Math.Abs(value - target) <= Math.Abs(target) * 2e-4 + 1e-9;
}

/// <summary>
/// A part of the picture with a level of its own, as its section on the screen has it. The rest
/// of a level - the anti-aliasing, the post-processing switches, the dynamic lights and the smoke - goes only with the whole.
/// </summary>
/// <param name="Keys">The eu07.ini keys it sets.</param>
public sealed record GraphicsGroup(IReadOnlyList<string> Keys)
{
    public static readonly GraphicsGroup DrawRange = new(new[] { "splinefidelity", "gfx.drawrange.factor.max" });

    public static readonly GraphicsGroup Textures = new(new[]
    {
        "maxtexturesize", "maxcabtexturesize", "anisotropicfiltering"
    });

    public static readonly GraphicsGroup Shadows = new(new[]
    {
        "shadows", "gfx.shadowmap.enabled", "shadowtune", "gfx.shadow.rank.cutoff", "gfx.shadow.angle.min", "gfx.shadows.cab.range"
    });

    public static readonly GraphicsGroup Reflections = new(new[]
    {
        "gfx.envmap.enabled", "gfx.reflections.fidelity", "gfx.reflections.framerate"
    });

    public static readonly IReadOnlyList<GraphicsGroup> All = new[]
    {
        DrawRange, Textures, Shadows, Reflections
    };
}

/// <summary>What applying a level touched.</summary>
/// <param name="ChosenKeys">The eu07.ini keys it chose as a whole.</param>
public sealed record PresetApplied(IReadOnlyList<string> ChosenKeys);

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

        var chosen = new List<string>(GraphicsPreset.Keys);
        if (preset.Extras is not null || coming is not null)
            chosen.AddRange(GraphicsExtras.Keys);
        if (preset.Top is not null || comingTop is not null)
            chosen.AddRange(GraphicsTop.Keys);
        return new PresetApplied(chosen);
    }
}
