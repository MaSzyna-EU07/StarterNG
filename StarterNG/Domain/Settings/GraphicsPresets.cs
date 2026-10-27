using System;

namespace StarterNG.Domain.Settings;

public enum GraphicsPreset { Lowest, Low, Medium, High, Ultra, Custom }

/// <summary>
/// Quality presets of the starter: one set of graphics settings each. A preset
/// leaves alone what is a matter of taste or of the machine rather than of quality
/// (resolution, render scale, vsync, field of view, motion blur, chromatic
/// aberration) and keeps DLSS when it is on, since that is a choice of the
/// anti-aliasing the presets cannot make better. Ray tracing is only for Vulkan.
/// </summary>
public static class GraphicsPresets
{
    public static readonly GraphicsPreset[] All =
    {
        GraphicsPreset.Lowest, GraphicsPreset.Low, GraphicsPreset.Medium, GraphicsPreset.High, GraphicsPreset.Ultra
    };

    /// <summary>The settings a preset governs. Doubles are kept in hundredths so equality is exact.</summary>
    private sealed record Quality(
        int Texture, int CabTexture, int Anisotropy, AntiAliasingMode AntiAliasing,
        int DrawRange, int Spline, int DynamicLights,
        bool Shadows, int ShadowMap, int ShadowRange, int ShadowRank, int LightShadows, int ContactShadows,
        int Ao, int AoSlices, int AoSteps,
        int ReflectionsFidelity, int ReflectionsFramerate, int Ssr, bool RayTracing,
        bool VolumetricFog, int FogQuality, int Clouds, bool CloudShadows,
        bool Grass, int GrassDistance, int GrassDensityPercent,
        int SmokeFidelity, int Precipitation, bool Bloom, bool ExtraEffects);

    private static Quality Table(GraphicsPreset preset) => preset switch
    {
        GraphicsPreset.Lowest => new Quality(
            1024, 1024, 1, AntiAliasingMode.Off,
            1, 1, 3,
            false, 1024, 75, 1, 0, 0,
            0, 3, 4,
            0, 5, 0, false,
            false, 1, 0, false,
            false, 60, 100,
            1, 16384, false, false),
        GraphicsPreset.Low => new Quality(
            2048, 2048, 4, AntiAliasingMode.Msaa2,
            2, 2, 5,
            true, 1024, 150, 2, 1, 0,
            1, 3, 4,
            0, 10, 1, false,
            true, 1, 1, false,
            true, 40, 50,
            1, 32768, true, true),
        GraphicsPreset.Medium => new Quality(
            4096, 4096, 8, AntiAliasingMode.Msaa2,
            3, 3, 7,
            true, 2048, 250, 3, 2, 0,
            2, 3, 4,
            1, 30, 2, false,
            true, 2, 2, true,
            true, 60, 100,
            2, 65536, true, true),
        GraphicsPreset.High => new Quality(
            4096, 4096, 16, AntiAliasingMode.Msaa4,
            3, 4, 7,
            true, 4096, 300, 3, 3, 1,
            2, 4, 6,
            2, 30, 2, false,
            true, 3, 3, true,
            true, 90, 100,
            3, 65536, true, true),
        GraphicsPreset.Ultra => new Quality(
            8192, 8192, 16, AntiAliasingMode.Msaa8,
            3, 4, 7,
            true, 8192, 400, 3, 3, 3,
            2, 6, 8,
            2, 60, 3, true,
            true, 4, 4, true,
            true, 120, 150,
            4, 131072, true, true),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
    };

    /// <summary>A preset as it lands on these settings: DLSS kept where it runs, ray tracing on Vulkan only.</summary>
    private static Quality Resolved(GraphicsPreset preset, SimulatorSettings s, bool dlssPlatform)
    {
        var q = Table(preset);
        if (GraphicsRules.DlssActive(s, dlssPlatform))
            q = q with { AntiAliasing = s.AntiAliasing };
        if (!GraphicsRules.RayTracingAllowed(s))
            q = q with { RayTracing = false };
        return q;
    }

    private static Quality Of(SimulatorSettings s) => new(
        s.MaxTextureSize, s.MaxCabTextureSize, s.TextureFiltering, s.AntiAliasing,
        (int)Math.Round(s.DrawRangeFactor), s.SplineFidelity, s.DynamicLights,
        s.RenderShadows, s.ShadowMapResolution, s.ShadowProjectionRange, s.ShadowRankCutoff, s.LightShadows, s.ContactShadows,
        s.AmbientOcclusion, s.AoSlices, s.AoSteps,
        s.ReflectionsFidelity, s.ReflectionsFramerate, s.Ssr, s.RayTracing,
        s.VolumetricFog, s.FogQuality, s.Clouds, s.CloudShadows,
        s.Grass, s.GrassDistance, (int)Math.Round(s.GrassDensity * 100),
        s.SmokeFidelity, s.PrecipitationParticles, s.Bloom, s.ExtraEffects);

    public static void Apply(SimulatorSettings s, GraphicsPreset preset, bool dlssPlatform)
    {
        if (preset == GraphicsPreset.Custom)
            return;

        var q = Resolved(preset, s, dlssPlatform);
        s.MaxTextureSize = q.Texture;
        s.MaxCabTextureSize = q.CabTexture;
        s.TextureFiltering = q.Anisotropy;
        s.AntiAliasing = q.AntiAliasing;
        s.DrawRangeFactor = q.DrawRange;
        s.SplineFidelity = q.Spline;
        s.DynamicLights = q.DynamicLights;
        s.RenderShadows = q.Shadows;
        s.ShadowMapResolution = q.ShadowMap;
        s.ShadowProjectionRange = q.ShadowRange;
        s.ShadowRankCutoff = q.ShadowRank;
        s.LightShadows = q.LightShadows;
        s.ContactShadows = q.ContactShadows;
        s.AmbientOcclusion = q.Ao;
        s.AoSlices = q.AoSlices;
        s.AoSteps = q.AoSteps;
        s.ReflectionsFidelity = q.ReflectionsFidelity;
        s.ReflectionsFramerate = q.ReflectionsFramerate;
        s.Ssr = q.Ssr;
        s.RayTracing = q.RayTracing;
        s.VolumetricFog = q.VolumetricFog;
        s.FogQuality = q.FogQuality;
        s.Clouds = q.Clouds;
        s.CloudShadows = q.CloudShadows;
        s.Grass = q.Grass;
        s.GrassDistance = q.GrassDistance;
        s.GrassDensity = q.GrassDensityPercent / 100.0;
        s.SmokeFidelity = q.SmokeFidelity;
        s.PrecipitationParticles = q.Precipitation;
        s.Bloom = q.Bloom;
        s.ExtraEffects = q.ExtraEffects;
    }

    /// <summary>The preset these settings are exactly, or Custom.</summary>
    public static GraphicsPreset Detect(SimulatorSettings s, bool dlssPlatform)
    {
        var current = Of(s);
        foreach (var preset in All)
            if (Resolved(preset, s, dlssPlatform) == current)
                return preset;
        return GraphicsPreset.Custom;
    }
}
